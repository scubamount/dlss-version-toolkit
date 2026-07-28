namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;
using System.Diagnostics;

public interface IUpgradeService
{
    UpgradeOperation UpgradeFromStaging(string ngxBasePath);
    UpgradeOperation SyncToNGX(string sourcePath, string sourceType, string ngxBasePath);
    UpgradeOperation SyncFromDlssSDK(string zipPath, string ngxBasePath);
    UpgradeOperation ApplyToAnWave(string anWavePath, string ngxBasePath);
}

public class UpgradeService : IUpgradeService
{
    private readonly INgxScanner _ngxScanner;
    private readonly IBackupService _backupService;
    private static readonly string ReleaseSubPath = @"models\dlss_override\versions";
    
    // Public so tests can assert coverage: v0.0.43 audit found nvngx_deepdvc.dll missing
    // here, so DeepDVC was never synced and stayed stale forever while the other three updated.
    public static readonly string[] NgxDllNames = { "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll", "nvngx_deepdvc.dll" };
    private static readonly string[] AllowedPrefixes;

    static UpgradeService()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var paths = new List<string>();
        if (!string.IsNullOrEmpty(programData))
            paths.Add(Path.Combine(programData, "NVIDIA", "NGX"));
        if (!string.IsNullOrEmpty(appData))
            paths.Add(Path.Combine(appData, "NVIDIA", "NGX"));
        AllowedPrefixes = paths.ToArray();
    }

    public UpgradeService(INgxScanner ngxScanner, IBackupService backupService)
    {
        _ngxScanner = ngxScanner;
        _backupService = backupService;
    }

    public UpgradeOperation UpgradeFromStaging(string ngxBasePath)
    {
        // Auto-detect default NGX path if not configured
        if (string.IsNullOrEmpty(ngxBasePath))
        {
            ngxBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "NVIDIA", "NGX");
        }

        if (!IsPathAllowed(ngxBasePath))
        {
            return new UpgradeOperation
            {
                Status = OperationStatus.Failed,
                ErrorMessage = "Path not in allowed list."
            };
        }

        var operation = new UpgradeOperation
        {
            SourceType = "Staging",
            TargetType = "NGX_Release"
        };

        var releases = _ngxScanner.Scan(ngxBasePath).Where(e => e.Source == "NGX_Release").ToList();
        var stagings = _ngxScanner.Scan(ngxBasePath).Where(e => e.Source == "NGX_Staging").ToList();

        if (releases.Count == 0)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = "No Release version found";
            return operation;
        }

        if (stagings.Count == 0)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = "No staging versions available for upgrade";
            return operation;
        }

        var latestRelease = releases.OrderByDescending(e => TryParseVersion(e.DLSS) ?? new Version(0, 0)).First();
        var latestStaging = stagings.OrderByDescending(e => TryParseVersion(e.DLSS) ?? new Version(0, 0)).First();

        if (!IsVersionNewer(latestStaging.DLSS, latestRelease.DLSS))
        {
            operation.Status = OperationStatus.Completed;
            operation.ErrorMessage = $"Release is already up to date ({latestRelease.DLSS} >= {latestStaging.DLSS})";
            return operation;
        }

        operation.SourcePath = latestStaging.Path;
        operation.TargetPath = latestRelease.Path;

        return PerformUpgrade(operation, ngxBasePath, latestStaging);
    }

    public UpgradeOperation SyncToNGX(string sourcePath, string sourceType, string ngxBasePath)
    {
        // Collect NGX candidate paths (explicit path first, then default known paths)
        var candidates = GetNgxCandidatePaths(ngxBasePath);

        // Find NGX Release across all candidate paths (similar to ScanService.ScanAllAsync)
        var ngxScanner = new NgxScanner(new NgxConfigParser());
        List<DLSSVersionEntry>? releases = null;
        string? foundPath = null;

        foreach (var candidate in candidates)
        {
            try
            {
                var found = ngxScanner.Scan(candidate).Where(e => e.Source == "NGX_Release").ToList();
                if (found.Count > 0)
                {
                    releases = found;
                    foundPath = candidate;
                    break;
                }
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"SyncToNGX: access denied scanning NGX path: {candidate}");
            }
        }

        if (releases == null || releases.Count == 0)
        {
            return new UpgradeOperation
            {
                Status = OperationStatus.Failed,
                ErrorMessage = "No Release version found"
            };
        }

        if (!IsPathAllowed(foundPath!))
        {
            return new UpgradeOperation
            {
                Status = OperationStatus.Failed,
                ErrorMessage = "Target NGX path not in allowed list."
            };
        }

        var operation = new UpgradeOperation
        {
            SourceType = sourceType,
            TargetType = "NGX_Release",
            SourcePath = sourcePath,
            TargetPath = releases.OrderByDescending(e => TryParseVersion(e.DLSS) ?? new Version(0, 0)).First().Path
        };

        var sourceVersions = ReadSourceVersions(sourcePath, sourceType);
        if (sourceVersions == null)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = "Required DLLs not found in source path";
            return operation;
        }

	var latestRelease = releases.OrderByDescending(e => TryParseVersion(e.DLSS) ?? new Version(0, 0)).First();

	if (!IsVersionNewer(sourceVersions.DLSS, latestRelease.DLSS))
	{
// Same version — but still sync if target DLLs are missing (incomplete install)
var targetDllExists = File.Exists(Path.Combine(operation.TargetPath, "nvngx_dlss.dll"));
		if (targetDllExists)
		{
			operation.Status = OperationStatus.Completed;
			operation.ErrorMessage = "Source is not newer than NGX Release";
			return operation;
		}
		// DLLs missing — fall through to PerformSync to recreate them
	}

        return PerformSync(operation, foundPath!, sourceVersions);
    }

    public UpgradeOperation SyncFromDlssSDK(string zipPath, string ngxBasePath)
    {
        // Auto-detect default NGX path if not configured
        if (string.IsNullOrEmpty(ngxBasePath))
        {
            ngxBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "NVIDIA", "NGX");
        }

        if (!File.Exists(zipPath))
        {
            return new UpgradeOperation
            {
                Status = OperationStatus.Failed,
                ErrorMessage = $"DLSS SDK zip not found: {zipPath}"
            };
        }

        if (!IsPathAllowed(ngxBasePath))
        {
            return new UpgradeOperation
            {
                Status = OperationStatus.Failed,
                ErrorMessage = "Target NGX path not in allowed list."
            };
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"DLSSVersionToolkit_DlssSDK_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, tempDir);

            // Look for DLLs in DLSS/bin/Win64/ first, then fall back to any nvngx_*.dll
            string? binPath = FindDllFolder(tempDir);
            if (binPath == null)
            {
                return new UpgradeOperation
                {
                    Status = OperationStatus.Failed,
                    ErrorMessage = "Could not find nvngx DLLs inside the downloaded SDK zip."
                };
            }

            return SyncToNGX(binPath, "DlssSDK", ngxBasePath);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch (Exception ex) { Debug.WriteLine($"SyncFromDlssSDK: temp dir cleanup failed: {ex.Message}"); }
        }
    }

    private static List<string> GetNgxCandidatePaths(string? ngxBasePath)
    {
        // Delegates to the shared resolver (v0.0.38): explicit → driver registry → defaults.
        return NgxPathResolver.GetCandidatePaths(ngxBasePath);
    }

    private static string? FindDllFolder(string rootDir)
    {
        // Known layouts, newest first:
        //   - NVIDIA/DLSS demo zip (v310.7.0): DLSS_Sample_App/bin/ngx_dlss_demo/nvngx_dlss.dll
        //   - older demo zips:                 DLSS/bin/Win64/nvngx_dlss.dll
        //   - NVIDIA-RTX/Streamline SDK:        bin/x64/nvngx_dlss.dll (handled by StreamlineDownloadService)
        // Verified against the actual v310.7.0 artifact — the demo zip ships ONLY nvngx_dlss.dll
        // (no dlssg/dlssd) under DLSS_Sample_App/bin/ngx_dlss_demo/.
        var knownRelative = new[]
        {
            Path.Combine("DLSS_Sample_App", "bin", "ngx_dlss_demo"),
            Path.Combine("DLSS", "bin", "Win64"),
            Path.Combine("bin", "x64"),
        };
        foreach (var rel in knownRelative)
        {
            var candidate = Path.Combine(rootDir, rel);
            if (File.Exists(Path.Combine(candidate, "nvngx_dlss.dll")))
                return candidate;
        }

        // Fall back: find any nvngx_dlss.dll anywhere in the extracted folder
        var found = Directory.GetFiles(rootDir, "nvngx_dlss.dll", SearchOption.AllDirectories).FirstOrDefault();
        if (found != null) return Path.GetDirectoryName(found);

        return null;
    }

    public UpgradeOperation ApplyToAnWave(string anWavePath, string ngxBasePath)
    {
        // Auto-detect NGX base path if not configured
        if (string.IsNullOrEmpty(ngxBasePath))
        {
            ngxBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "NVIDIA", "NGX");
        }

        if (string.IsNullOrEmpty(anWavePath))
        {
            return new UpgradeOperation
            {
                Status = OperationStatus.Failed,
                ErrorMessage = "AnWave path is not configured."
            };
        }

        if (!IsPathAllowed(ngxBasePath))
        {
            return new UpgradeOperation
            {
                Status = OperationStatus.Failed,
                ErrorMessage = "NGX path not in allowed list."
            };
        }

	if (!Directory.Exists(anWavePath))
	{
		return new UpgradeOperation
		{
			Status = OperationStatus.Failed,
			ErrorMessage = $"AnWave folder not found: {anWavePath}"
		};
	}

	// Pre-flight: check AnWave directory is writable
	if (!OperationGuard.IsDirectoryWritable(anWavePath))
	{
		return new UpgradeOperation
		{
			Status = OperationStatus.Failed,
			ErrorMessage = $"AnWave directory is not writable: {anWavePath}"
		};
	}

        var operation = new UpgradeOperation
        {
            SourceType = "NGX_Release",
            TargetType = "AnWave",
            SourcePath = ngxBasePath
        };

        // Find NGX Release version folder
        var releases = _ngxScanner.Scan(ngxBasePath).Where(e => e.Source == "NGX_Release").ToList();
        if (releases.Count == 0)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = "No NGX Release version found";
            return operation;
        }

        var latestRelease = releases.OrderByDescending(e => TryParseVersion(e.DLSS) ?? new Version(0, 0)).First();
        operation.TargetPath = anWavePath;

        // Find DLLs in NGX Release folder
        var ngxlDlss = FindDll(latestRelease.Path, "nvngx_dlss.dll");
        if (ngxlDlss == null)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = "Could not find nvngx_dlss.dll in NGX Release";
            return operation;
        }

        // Build list of DLLs and config to copy to AnWave
        var ngxFolder = Path.GetDirectoryName(ngxlDlss)!;
        var ngxConfig = FindConfig(ngxFolder);

        operation.Status = OperationStatus.InProgress;

        try
        {
            // Copy DLSS DLLs to AnWave folder (root level) — same component set as NGX sync.
            var dllsToCopy = NgxDllNames
                .Select(n => (n, Path.Combine(anWavePath, n)))
                .ToArray();

            foreach (var (dllName, destPath) in dllsToCopy)
            {
		var srcDll = FindDll(ngxFolder, dllName);
		if (srcDll != null && File.Exists(srcDll))
		{
			if (!OperationGuard.VerifyDllSignature(srcDll))
				throw new InvalidOperationException($"DLL {dllName} failed signature verification");

			var srcSize = new FileInfo(srcDll).Length;
			File.Copy(srcDll, destPath, true);

			// Post-copy verification: check file size matches
			if (!OperationGuard.VerifyFile(destPath, srcSize))
				throw new InvalidOperationException($"Post-copy verification failed for {dllName}");

			operation.FilesCopied.Add(dllName);
                }
            }

            // Copy config if found
            if (ngxConfig != null)
            {
                var destConfig = Path.Combine(anWavePath, "nvngx_package_config.txt");
                File.Copy(ngxConfig, destConfig, true);
                operation.FilesCopied.Add("nvngx_package_config.txt");
            }

            operation.Status = OperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = ex.Message;
        }

        return operation;
    }

    private UpgradeOperation PerformUpgrade(UpgradeOperation operation, string ngxBasePath, DLSSVersionEntry staging)
    {
        operation.Status = OperationStatus.InProgress;
        var releaseVersionsPath = Path.Combine(ngxBasePath, ReleaseSubPath);

	var backupPath = _backupService.CreateBackup(operation.TargetPath, releaseVersionsPath);
	if (backupPath == null)
	{
		operation.Status = OperationStatus.Failed;
		operation.ErrorMessage = "Failed to create backup";
		return operation;
	}

	// Verify backup was created successfully
	if (!_backupService.VerifyBackup(backupPath))
	{
		operation.Status = OperationStatus.Failed;
		operation.ErrorMessage = "Backup verification failed — backup directory is empty or invalid.";
		return operation;
	}

	operation.BackupPath = backupPath;
        try
        {
		foreach (var dll in NgxDllNames)
		{
			var srcDll = FindDll(staging.Path, dll);
			if (srcDll == null) continue;

			var destDll = FindDll(operation.TargetPath, dll)
				?? Path.Combine(operation.TargetPath, dll);

			if (!OperationGuard.VerifyDllSignature(srcDll))
			{
				throw new InvalidOperationException($"DLL {dll} failed signature verification");
			}
			File.Copy(srcDll, destDll, true);
			operation.FilesCopied.Add(dll);
		}

		var srcConfig = FindConfig(staging.Path);
		var destConfig = FindConfig(operation.TargetPath)
			?? Path.Combine(operation.TargetPath, "nvngx_package_config.txt");
		if (srcConfig != null)
		{
			File.Copy(srcConfig, destConfig, true);
			operation.FilesCopied.Add("nvngx_package_config.txt");
		}

            if (!VerifyCopiedFiles(staging.Path, operation.TargetPath))
            {
                throw new InvalidOperationException("Post-copy verification failed");
            }

            operation.Status = OperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = ex.Message;

            var restored = _backupService.RestoreBackup(operation.BackupPath, operation.TargetPath);
            if (restored)
                operation.Status = OperationStatus.RolledBack;
        }

        return operation;
    }

    private UpgradeOperation PerformSync(UpgradeOperation operation, string ngxBasePath, DLSSVersionEntry source)
    {
        operation.Status = OperationStatus.InProgress;
        var releaseVersionsPath = Path.Combine(ngxBasePath, ReleaseSubPath);

	var backupPath = _backupService.CreateBackup(operation.TargetPath, releaseVersionsPath);
	if (backupPath == null)
	{
		operation.Status = OperationStatus.Failed;
		operation.ErrorMessage = "Failed to create backup";
		return operation;
	}

	// Verify backup was created successfully
	if (!_backupService.VerifyBackup(backupPath))
	{
		operation.Status = OperationStatus.Failed;
		operation.ErrorMessage = "Backup verification failed — backup directory is empty or invalid.";
		return operation;
	}

	operation.BackupPath = backupPath;
        try
        {
            var binPath = ResolveBinPath(operation.SourcePath);

			foreach (var dll in NgxDllNames)
			{
				var srcDll = Path.Combine(binPath, dll);
				if (!File.Exists(srcDll)) continue;

				var destDll = FindDll(operation.TargetPath, dll)
					?? Path.Combine(operation.TargetPath, dll);

				if (!OperationGuard.VerifyDllSignature(srcDll))
				{
					throw new InvalidOperationException($"DLL {dll} failed signature verification");
				}
				File.Copy(srcDll, destDll, true);
				operation.FilesCopied.Add(dll);
			}

		var srcConfig = Path.Combine(binPath, "nvngx_package_config.txt");
		var destConfig = FindConfig(operation.TargetPath)
			?? Path.Combine(operation.TargetPath, "nvngx_package_config.txt");
		if (File.Exists(srcConfig))
		{
			File.Copy(srcConfig, destConfig, true);
			operation.FilesCopied.Add("nvngx_package_config.txt");
		}

            if (!VerifyCopiedFiles(binPath, operation.TargetPath))
            {
                throw new InvalidOperationException("Post-copy verification failed");
            }

            operation.Status = OperationStatus.Completed;
            operation.CompletedAt = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = ex.Message;

            var restored = _backupService.RestoreBackup(operation.BackupPath, operation.TargetPath);
            if (restored)
                operation.Status = OperationStatus.RolledBack;
        }

        return operation;
    }

    private static bool IsPathAllowed(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        var normalized = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return AllowedPrefixes.Any(prefix =>
            normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool VerifyCopiedFiles(string srcFolder, string destFolder)
    {
        foreach (var dll in NgxDllNames)
        {
            var srcDll = FindDll(srcFolder, dll);
            var destDll = FindDll(destFolder, dll);
            if (srcDll != null && destDll != null)
            {
                var srcInfo = new FileInfo(srcDll);
                var destInfo = new FileInfo(destDll);
                if (!destInfo.Exists || destInfo.Length == 0 || destInfo.Length != srcInfo.Length)
return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Resolves the folder that actually contains nvngx_dlss.dll. Accepts either the SDK
    /// root or the bin\x64 folder itself (v0.0.41: callers pass bin\x64 directly, and the
    /// old unconditional Path.Combine(sourcePath, "bin", "x64") for StreamlineSDK produced
    /// ...\bin\x64\bin\x64 — so every Streamline sync failed silently with "Required DLLs
    /// not found" and dlssg/dlssd/deepdvc were never updated).
    /// </summary>
    public static string ResolveBinPath(string sourcePath)
    {
        if (File.Exists(Path.Combine(sourcePath, "nvngx_dlss.dll"))) return sourcePath;
        var sub = Path.Combine(sourcePath, "bin", "x64");
        return File.Exists(Path.Combine(sub, "nvngx_dlss.dll")) ? sub : sourcePath;
    }

    private static DLSSVersionEntry? ReadSourceVersions(string sourcePath, string sourceType)
    {
        var binPath = ResolveBinPath(sourcePath);

        var mainDll = Path.Combine(binPath, "nvngx_dlss.dll");
        if (!File.Exists(mainDll))
            return null;

        var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(mainDll);
        var version = vi.FileVersion?.Replace(',', '.') ?? "Unknown";

        return new DLSSVersionEntry
        {
            Source = sourceType,
            BuildID = version,
            DLSS = version,
            Path = sourcePath
        };
    }

    private static string? FindDll(string folder, string dllName)
    {
        try
        {
            return Directory.GetFiles(folder, dllName, SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch { return null; }
    }

    private static string? FindConfig(string folder)
    {
        try
        {
            return Directory.GetFiles(folder, "nvngx_package_config.txt", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch { return null; }
    }

    private static bool IsVersionNewer(string version1, string version2)
    {
        if (version1 == "Unknown" || version1 == "N/A") return false;
        if (version2 == "Unknown" || version2 == "N/A") return true;

        try
        {
            var v1 = NormalizeVersion(version1);
            var v2 = NormalizeVersion(version2);

            var parts1 = v1.Split('.').Take(4).Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
            var parts2 = v2.Split('.').Take(4).Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();

            for (int i = 0; i < 4; i++)
            {
                if (parts1[i] > parts2[i]) return true;
                if (parts1[i] < parts2[i]) return false;
            }
            return false;
        }
        catch { return false; }
    }

    private static Version? TryParseVersion(string version)
    {
        try
        {
            var parts = NormalizeVersion(version).Split('.');
            var major = int.TryParse(parts.ElementAtOrDefault(0), out var m) ? m : 0;
            var minor = int.TryParse(parts.ElementAtOrDefault(1), out var n) ? n : 0;
            var build = int.TryParse(parts.ElementAtOrDefault(2), out var b) ? b : 0;
            var rev = int.TryParse(parts.ElementAtOrDefault(3), out var r) ? r : 0;
            return new Version(major, minor, build, rev);
        }
        catch { return null; }
    }

    private static string NormalizeVersion(string version)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(version, @"[a-zA-Z]", "");
        var parts = cleaned.Split('.').Take(4);
        return string.Join(".", parts);
    }
}