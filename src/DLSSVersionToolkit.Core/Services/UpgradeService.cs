namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;

public interface IUpgradeService
{
    UpgradeOperation UpgradeFromStaging(string ngxBasePath);
    UpgradeOperation SyncToNGX(string sourcePath, string sourceType, string ngxBasePath);
    UpgradeOperation SyncFromDlssSDK(string zipPath, string ngxBasePath);
}

public class UpgradeService : IUpgradeService
{
    private readonly INgxScanner _ngxScanner;
    private readonly IBackupService _backupService;
    private static readonly string ReleaseSubPath = @"models\dlss_override\versions";
    
    private static readonly string[] NgxDllNames = { "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll" };
    private static readonly string[] AllowedPrefixes;

    static UpgradeService()
    {
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        AllowedPrefixes = new[]
        {
            Path.Combine(programData, "NVIDIA", "NGX"),
            Path.Combine(appData, "NVIDIA", "NGX"),
        };
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

        var latestRelease = releases.OrderByDescending(e => ParseVersion(e.DLSS)).First();
        var latestStaging = stagings.OrderByDescending(e => ParseVersion(e.DLSS)).First();

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
                ErrorMessage = "Target NGX path not in allowed list."
            };
        }

        var operation = new UpgradeOperation
        {
            SourceType = sourceType,
            TargetType = "NGX_Release",
            SourcePath = sourcePath
        };

        var releases = _ngxScanner.Scan(ngxBasePath).Where(e => e.Source == "NGX_Release").ToList();
        if (releases.Count == 0)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = "No Release version found";
            return operation;
        }

        var latestRelease = releases.OrderByDescending(e => ParseVersion(e.DLSS)).First();
        operation.TargetPath = latestRelease.Path;

        var sourceVersions = ReadSourceVersions(sourcePath, sourceType);
        if (sourceVersions == null)
        {
            operation.Status = OperationStatus.Failed;
            operation.ErrorMessage = "Required DLLs not found in source path";
            return operation;
        }

        if (!IsVersionNewer(sourceVersions.DLSS, latestRelease.DLSS))
        {
            operation.Status = OperationStatus.Completed;
            operation.ErrorMessage = $"Source is not newer than NGX Release";
            return operation;
        }

        return PerformSync(operation, ngxBasePath, sourceVersions);
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
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static string? FindDllFolder(string rootDir)
    {
        // Try DLSS/bin/Win64/ first
        var dllPath = Path.Combine(rootDir, "DLSS", "bin", "Win64", "nvngx_dlss.dll");
        if (File.Exists(dllPath)) return Path.Combine(rootDir, "DLSS", "bin", "Win64");

        // Fall back: find any nvngx_dlss.dll anywhere in the extracted folder
        var found = Directory.GetFiles(rootDir, "nvngx_dlss.dll", SearchOption.AllDirectories).FirstOrDefault();
        if (found != null) return Path.GetDirectoryName(found);

        return null;
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
        operation.BackupPath = backupPath;

        try
        {
            foreach (var dll in NgxDllNames)
            {
                var srcDll = FindDll(staging.Path, dll);
                var destDll = FindDll(operation.TargetPath, dll);
                if (srcDll != null && destDll != null)
                {
                    if (!VerifyDllSignature(srcDll))
                    {
                        throw new InvalidOperationException($"DLL {dll} failed signature verification");
                    }
                    File.Copy(srcDll, destDll, true);
                    operation.FilesCopied.Add(dll);
                }
            }

            var srcConfig = FindConfig(staging.Path);
            var destConfig = FindConfig(operation.TargetPath);
            if (srcConfig != null && destConfig != null)
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
        operation.BackupPath = backupPath;

        try
        {
            var binPath = operation.SourceType == "StreamlineSDK"
                ? Path.Combine(operation.SourcePath, "bin", "x64")
                : operation.SourcePath;

            foreach (var dll in NgxDllNames)
            {
                var srcDll = Path.Combine(binPath, dll);
                var destDll = FindDll(operation.TargetPath, dll);
                if (File.Exists(srcDll) && destDll != null)
                {
                    if (!VerifyDllSignature(srcDll))
                    {
                        throw new InvalidOperationException($"DLL {dll} failed signature verification");
                    }
                    File.Copy(srcDll, destDll, true);
                    operation.FilesCopied.Add(dll);
                }
            }

            var srcConfig = Path.Combine(binPath, "nvngx_package_config.txt");
            var destConfig = FindConfig(operation.TargetPath);
            if (File.Exists(srcConfig) && destConfig != null)
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

    private static bool VerifyDllSignature(string dllPath)
    {
        try
        {
            var fi = new FileInfo(dllPath);
            if (!fi.Exists || fi.Length < 1024) return false;
#pragma warning disable CA2022
            using var fs = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[2];
            _ = fs.Read(header, 0, 2);
#pragma warning restore CA2022
            return header[0] == 'M' && header[1] == 'Z';
        }
        catch { return false; }
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
                if (!destInfo.Exists || destInfo.Length != srcInfo.Length)
                    return false;
            }
        }
        return true;
    }

    private static DLSSVersionEntry? ReadSourceVersions(string sourcePath, string sourceType)
    {
        var binPath = sourceType == "StreamlineSDK"
            ? Path.Combine(sourcePath, "bin", "x64")
            : sourcePath;

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
            return Directory.GetFiles(folder, dllName, SearchOption.AllDirectories).FirstOrDefault();
        }
        catch { return null; }
    }

    private static string? FindConfig(string folder)
    {
        try
        {
            return Directory.GetFiles(folder, "nvngx_package_config.txt", SearchOption.AllDirectories).FirstOrDefault();
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

    private static double ParseVersion(string version)
    {
        try
        {
            var parts = NormalizeVersion(version).Split('.');
            var major = int.TryParse(parts.ElementAtOrDefault(0), out var m) ? m : 0;
            var minor = int.TryParse(parts.ElementAtOrDefault(1), out var n) ? n : 0;
            return major * 1000 + minor;
        }
        catch { return 0; }
    }

    private static string NormalizeVersion(string version)
    {
        var cleaned = System.Text.RegularExpressions.Regex.Replace(version, @"[a-zA-Z]", "");
        var parts = cleaned.Split('.').Take(4);
        return string.Join(".", parts);
    }
}