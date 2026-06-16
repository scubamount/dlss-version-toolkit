namespace DLSSVersionToolkit.Core.Services;

using System.IO;
using DLSSVersionToolkit.Core.Models;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;

public class AnWaveSetupResult
{
    public bool Success { get; set; }
    public string? InstalledPath { get; set; }
    public string? ErrorMessage { get; set; }
    public string? GlomVersion { get; set; }
    public string? DllVersion { get; set; }
}

public class AnWaveAutoApplyResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> FilesCopied { get; set; } = new();
    public bool ConfigWritten { get; set; }
    public string? AppliedVersion { get; set; }
}

public interface IAnWaveAutoService
{
    /// <summary>
    /// Downloads nvidiaDlssGlom, extracts it to an install folder under %APPDATA%, downloads DLSS DLLs
    /// from NVIDIA GitHub, places them alongside nvidiaDlssGlom.exe, and runs the "Update" operation
    /// by writing the nvngx_config.txt file to NGX (activating global DLSS override).
    /// </summary>
    Task<AnWaveSetupResult> SetupAnWaveAsync(IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Places the NGX Release DLLs into the AnWave folder and writes the config to activate override.
    /// Called after syncing SDK to NGX to keep AnWave in sync.
    /// </summary>
    Task<AnWaveAutoApplyResult> AutoApplyAsync(string anWavePath, string ngxBasePath, IProgress<int>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Returns the cached AnWave install path, or null if not yet installed.
    /// </summary>
    string? GetInstalledPath();

    /// <summary>
    /// Returns the installed nvidiaDlssGlom version string.
    /// </summary>
    string? GetInstalledGlomVersion();

    /// <summary>
    /// Returns the DLL version currently in the AnWave folder.
    /// </summary>
    string? GetInstalledDllVersion();

    /// <summary>
    /// Probes the known install directory on disk for an existing AnWave/nvidiaDlssGlom setup
    /// WITHOUT downloading or modifying anything. Populates the cached install path + DLL/glom
    /// versions so the UI reflects an install a previous run (or Update All) already made,
    /// instead of showing "not set" until the user re-runs Setup this session.
    /// </summary>
    AnWaveDetectionResult DetectInstalled();
}

/// <summary>Read-only result of probing disk for an existing AnWave install.</summary>
public class AnWaveDetectionResult
{
    public bool IsInstalled { get; set; }
    public string? InstalledPath { get; set; }
    public string? DllVersion { get; set; }
    public string? GlomVersion { get; set; }
}

public class AnWaveAutoService : IAnWaveAutoService
{
    private static readonly HttpClient _http;
    private static readonly string CacheDir;
    private static readonly string InstallDir;
    private static readonly string ConfigFilePath;

    private string? _installedPath;
    private string? _glomVersion;
    private string? _dllVersion;

    private const string GlomRepoApi = "https://api.github.com/repos/SimonMacer/AnWave/releases/tags/AnWave-DLSS";
    private static readonly Regex GlomVersionRegex = new(@"nvidiaDlssGlom-v([0-9.]+)-", RegexOptions.Compiled);

    static AnWaveAutoService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        CacheDir = Path.Combine(appData, "DLSSVersionToolkit", "AnWaveCache");
        InstallDir = Path.Combine(appData, "DLSSVersionToolkit", "AnWave");
        ConfigFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NVIDIA", "NGX", "nvngx_config.txt");

        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
    }

    public string? GetInstalledPath() => _installedPath;
    public string? GetInstalledGlomVersion() => _glomVersion;
    public string? GetInstalledDllVersion() => _dllVersion;

    /// <summary>
    /// Read-only probe of the install dir. Reads the real DLL/glom versions via FileVersionInfo
    /// and caches them, so a prior install is recognised without re-running Setup. Mirrors the
    /// existing-install fast path in <see cref="SetupAnWaveAsync"/> (which now delegates here).
    /// </summary>
    public AnWaveDetectionResult DetectInstalled()
    {
        var mainDll = Path.Combine(InstallDir, "nvngx_dlss.dll");
        if (!File.Exists(mainDll))
            return new AnWaveDetectionResult { IsInstalled = false };

        _installedPath = InstallDir;

        try
        {
            var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(mainDll);
            _dllVersion = vi.FileVersion ?? vi.ProductVersion ?? "unknown";
        }
        catch
        {
            _dllVersion ??= "unknown";
        }

        try
        {
            var glomExe = Directory.GetFiles(InstallDir, "nvidiaDlssGlom*.exe").FirstOrDefault();
            if (glomExe != null)
            {
                var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(glomExe);
                _glomVersion = vi.FileVersion ?? vi.ProductVersion ?? "cached";
            }
        }
        catch
        {
            _glomVersion ??= "cached";
        }

        return new AnWaveDetectionResult
        {
            IsInstalled = true,
            InstalledPath = _installedPath,
            DllVersion = _dllVersion,
            GlomVersion = _glomVersion
        };
    }

    public async Task<AnWaveSetupResult> SetupAnWaveAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        progress?.Report(0);

        // Quick check: if InstallDir already has the main DLL, skip re-download.
        // DetectInstalled() does the on-disk probe + version reads (single source of truth).
        var existing = DetectInstalled();
        if (existing.IsInstalled)
        {
            progress?.Report(100);
            return new AnWaveSetupResult
            {
                Success = true,
                InstalledPath = existing.InstalledPath,
                GlomVersion = existing.GlomVersion ?? "cached",
                DllVersion = existing.DllVersion ?? "unknown"
            };
        }

        // Step 1: Find and download latest nvidiaDlssGlom release
        progress?.Report(5);
        var glomUrl = await GetGlomDownloadUrl(ct);
        if (string.IsNullOrEmpty(glomUrl))
            return new AnWaveSetupResult { Success = false, ErrorMessage = "Could not find nvidiaDlssGlom release on GitHub." };

        progress?.Report(15);

        // Determine version from URL filename
        var versionMatch = GlomVersionRegex.Match(glomUrl);
        _glomVersion = versionMatch.Success ? versionMatch.Groups[1].Value : "unknown";

	// Pre-flight: network check (needed for GitHub API)
	if (!OperationGuard.IsNetworkAvailable())
	{
		// No network — check if we have a cached glom to work with
		if (!Directory.Exists(CacheDir) || Directory.GetFiles(CacheDir, "nvidiaDlssGlom*.rar").Length == 0)
			return new AnWaveSetupResult { Success = false, ErrorMessage = "No internet connection and no cached nvidiaDlssGlom found." };
	}

	// Pre-flight: disk space check (need ~300 MB for glom + DLSS SDK)
	if (!OperationGuard.HasDiskSpace(InstallDir, 300 * 1024 * 1024))
		return new AnWaveSetupResult { Success = false, ErrorMessage = "Insufficient disk space for AnWave setup (need at least 300 MB)." };

	// Pre-flight: ensure install directory is writable
	if (Directory.Exists(InstallDir) && !OperationGuard.IsDirectoryWritable(InstallDir))
		return new AnWaveSetupResult { Success = false, ErrorMessage = $"AnWave install directory is not writable: {InstallDir}" };

	// Create directories
	if (!Directory.Exists(CacheDir)) Directory.CreateDirectory(CacheDir);
	if (!Directory.Exists(InstallDir)) Directory.CreateDirectory(InstallDir);

	// Pre-flight: verify the install directory we just created is writable
	if (!OperationGuard.IsDirectoryWritable(InstallDir))
		return new AnWaveSetupResult { Success = false, ErrorMessage = $"Cannot write to AnWave install directory: {InstallDir}" };
        // Check if we already have this version cached
        var glomFileName = Path.GetFileName(glomUrl);
        var cachedGlomPath = Path.Combine(CacheDir, glomFileName);

        if (File.Exists(cachedGlomPath))
        {
            progress?.Report(30);
            // Already cached — just extract from cache instead of re-downloading
            return await ExtractGlomFromCacheAsync(cachedGlomPath, progress, ct);
        }

        // Download glom .rar to cache directory
        progress?.Report(20);
        var downloaded = await DownloadFileAsync(glomUrl, cachedGlomPath, ct);
        if (!downloaded)
            return new AnWaveSetupResult { Success = false, ErrorMessage = "Failed to download nvidiaDlssGlom." };

        // Housekeeping: trim old cached gloms, keep latest 2
        TrimGlomCache(2);

progress?.Report(30);
        return await ExtractGlomFromCacheAsync(cachedGlomPath, progress, ct);
    }

    private async Task<AnWaveSetupResult> ExtractGlomFromCacheAsync(string glomPath, IProgress<int>? progress, CancellationToken ct)
    {
	string? glomTmpDir = null;
	try
{
            // Clean up previous exe if any
            foreach (var oldExe in Directory.GetFiles(InstallDir, "nvidiaDlssGlom*.exe"))
            {
                try { File.Delete(oldExe); } catch { }
            }

			glomTmpDir = Path.Combine(Path.GetTempPath(), $"DLSSVT_glom_{Guid.NewGuid():N}");
			Directory.CreateDirectory(glomTmpDir);

            using var archive = ArchiveFactory.OpenArchive(glomPath);
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
				entry.WriteToDirectory(glomTmpDir, new ExtractionOptions { Overwrite = true });
            }

		// Move extracted files to install dir
		foreach (var file in Directory.GetFiles(glomTmpDir, "*", SearchOption.AllDirectories))
		{
			var srcInfo = new FileInfo(file);
			var dest = Path.Combine(InstallDir, Path.GetFileName(file));
			File.Copy(file, dest, true);

			// Post-copy verification: check file size matches
			if (!OperationGuard.VerifyFile(dest, srcInfo.Length))
				System.Diagnostics.Debug.WriteLine($"ExtractGlomFromCache: post-copy verification failed for {dest}");
		}

		// Verify nvidiaDlssGlom.exe exists after extraction
		var glomExe = Directory.GetFiles(InstallDir, "nvidiaDlssGlom*.exe", SearchOption.AllDirectories).FirstOrDefault();
		if (glomExe == null || !File.Exists(glomExe))
			return new AnWaveSetupResult { Success = false, ErrorMessage = "nvidiaDlssGlom.exe not found after extraction." };

	}
	catch (Exception ex)
	{
		return new AnWaveSetupResult { Success = false, ErrorMessage = $"Failed to extract nvidiaDlssGlom: {ex.Message}" };
	}
	finally
	{
		if (glomTmpDir != null) try { Directory.Delete(glomTmpDir, recursive: true); } catch { }
	}

        progress?.Report(55);

        // Step 2: Download DLSS DLLs from NVIDIA GitHub
        var ngxZipPath = Path.Combine(InstallDir, "ngx_dlss_demo.zip");

	// Network check before downloading DLSS DLLs from NVIDIA
	if (!OperationGuard.IsNetworkAvailable())
		return new AnWaveSetupResult { Success = false, ErrorMessage = "No internet connection — cannot download DLSS DLLs from NVIDIA." };

	// Download from NVIDIA/DLSS releases (latest)
	var latestRelease = await GetLatestNvidiaReleaseAsync(ct);
	if (string.IsNullOrEmpty(latestRelease))
		return new AnWaveSetupResult { Success = false, ErrorMessage = "Could not fetch latest NVIDIA/DLSS release." };

        _dllVersion = ExtractVersionFromUrl(latestRelease);
        var nvidiaDllUrl = latestRelease;

        progress?.Report(65);
        var dllDownloaded = await DownloadFileAsync(nvidiaDllUrl!, ngxZipPath, ct);
        if (!dllDownloaded)
            return new AnWaveSetupResult { Success = false, ErrorMessage = "Failed to download DLSS DLL package from NVIDIA." };

        progress?.Report(80);

		string? dllTmpDir = null;
		try
		{
			dllTmpDir = Path.Combine(Path.GetTempPath(), $"DLSSVT_dlls_{Guid.NewGuid():N}");
			Directory.CreateDirectory(dllTmpDir);
			System.IO.Compression.ZipFile.ExtractToDirectory(ngxZipPath, dllTmpDir, true);

		// Copy nvngx DLLs + config to install dir with verification
		foreach (var dll in Directory.GetFiles(dllTmpDir, "nvngx_*.dll", SearchOption.AllDirectories))
		{
			try
			{
				var srcSize = new FileInfo(dll).Length;
				var dest = Path.Combine(InstallDir, Path.GetFileName(dll));
				File.Copy(dll, dest, true);

				// Post-copy verification
				if (!OperationGuard.VerifyFile(dest, srcSize))
					System.Diagnostics.Debug.WriteLine($"ExtractGlomFromCache: DLL post-copy verification failed for {dest}");
			}
			catch (Exception ex_dll) { System.Diagnostics.Debug.WriteLine($"ExtractGlomFromCache: DLL copy failed: {ex_dll.Message}"); }
		}
		var cfg = Directory.GetFiles(dllTmpDir, "nvngx_package_config.txt", SearchOption.AllDirectories).FirstOrDefault();
		if (cfg != null)
		{
			try { File.Copy(cfg, Path.Combine(InstallDir, "nvngx_package_config.txt"), true); } catch { }
		}

		// Verify the main DLL has a valid PE signature
		var mainDll = Path.Combine(InstallDir, "nvngx_dlss.dll");
		if (File.Exists(mainDll) && !OperationGuard.VerifyDllSignature(mainDll))
			return new AnWaveSetupResult { Success = false, ErrorMessage = "Downloaded nvngx_dlss.dll failed PE signature verification." };

		// Clean up
	}
	catch (Exception ex)
	{
		return new AnWaveSetupResult { Success = false, ErrorMessage = $"Failed to extract DLSS DLLs: {ex.Message}" };
	}
	finally
	{
		if (dllTmpDir != null) try { Directory.Delete(dllTmpDir, recursive: true); } catch { }
	}

        progress?.Report(90);

        // Step 3: Write nvngx_config.txt to NGX to activate override
 WriteNgXConfig(_dllVersion);

        progress?.Report(100);

        _installedPath = InstallDir;

        return new AnWaveSetupResult
        {
            Success = true,
            InstalledPath = InstallDir,
            GlomVersion = _glomVersion,
            DllVersion = _dllVersion
        };
    }

    private void TrimGlomCache(int keepCount = 2)
    {
        if (!Directory.Exists(CacheDir)) return;

        var files = Directory.GetFiles(CacheDir, "nvidiaDlssGlom*.rar")
            .Select(f => new FileInfo(f))
            .Where(fi => fi.Exists)
            .OrderByDescending(fi => fi.CreationTimeUtc)
            .ToList();

        foreach (var file in files.Skip(keepCount))
        {
            try { File.Delete(file.FullName); } catch { }
        }
    }

    public Task<AnWaveAutoApplyResult> AutoApplyAsync(string anWavePath, string ngxBasePath, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        return Task.FromResult(AutoApplySync(anWavePath, ngxBasePath, progress));
    }

    private AnWaveAutoApplyResult AutoApplySync(string anWavePath, string ngxBasePath, IProgress<int>? progress = null)
    {
        progress?.Report(0);

        var result = new AnWaveAutoApplyResult();

        // Collect NGX candidate paths (explicit path first, then default known paths)
        var candidates = GetNgxCandidatePaths(ngxBasePath);

        // Find the NGX Release version folder across all candidate paths.
        // Prefer NGX_Release; fall back to NGX_Staging so a freshly-synced SDK that
        // landed in Staging still applies instead of failing outright.
        var ngxScanner = new NgxScanner(new NgxConfigParser());
        List<DLSSVersionEntry>? releases = null;
        var scannedPaths = new List<string>();

        foreach (var candidate in candidates)
        {
            scannedPaths.Add(candidate);
            try
            {
                var scanned = ngxScanner.Scan(candidate);
                var found = scanned.Where(e => e.Source == "NGX_Release").ToList();
                if (found.Count == 0)
                    found = scanned.Where(e => e.Source == "NGX_Staging").ToList();
                if (found.Count > 0)
                {
                    releases = found;
                    break;
                }
            }
            catch (UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine($"AutoApplySync: access denied scanning NGX path: {candidate}");
            }
        }

        if (releases == null || releases.Count == 0)
        {
            result.ErrorMessage = "No NGX Release version found. Searched: " +
                (scannedPaths.Count > 0 ? string.Join("; ", scannedPaths) : "(no candidate paths)");
            return result;
        }

        var latestRelease = releases.OrderByDescending(e => TryParseVersion(e.DLSS) ?? new Version(0, 0)).First();

        progress?.Report(20);

        // Find the DLLs in the NGX Release folder
        var ngxDll = Directory.GetFiles(latestRelease.Path, "nvngx_dlss.dll", SearchOption.AllDirectories).FirstOrDefault();
        var ngxFolder = ngxDll != null ? Path.GetDirectoryName(ngxDll) : null;

        if (ngxFolder == null || !Directory.Exists(ngxFolder))
        {
            result.ErrorMessage = $"Could not locate NGX Release DLL folder under: {latestRelease.Path}";
            return result;
        }

        // Copy DLLs and config to the AnWave folder
        var dllsToCopy = new[] { "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll" };

	foreach (var dllName in dllsToCopy)
	{
		var srcDll = Directory.GetFiles(ngxFolder, dllName, SearchOption.AllDirectories).FirstOrDefault();
		if (srcDll != null && File.Exists(srcDll))
		{
			// Pre-copy: verify source DLL has valid PE signature
			if (!OperationGuard.VerifyDllSignature(srcDll))
			{
				System.Diagnostics.Debug.WriteLine($"AutoApplySync: source DLL failed signature check: {srcDll}");
				continue; // Skip this DLL rather than fail the whole operation
			}

			var srcSize = new FileInfo(srcDll).Length;
			var destPath = Path.Combine(anWavePath, dllName);
			File.Copy(srcDll, destPath, true);

			// Post-copy verification: check file size matches
			if (!OperationGuard.VerifyFile(destPath, srcSize))
				System.Diagnostics.Debug.WriteLine($"AutoApplySync: post-copy verification failed for {destPath}");

			result.FilesCopied.Add(dllName);
            }
        }

        // Copy config
        var srcConfig = Directory.GetFiles(ngxFolder, "nvngx_package_config.txt", SearchOption.AllDirectories).FirstOrDefault();
        if (srcConfig != null)
        {
            var destConfig = Path.Combine(anWavePath, "nvngx_package_config.txt");
            File.Copy(srcConfig, destConfig, true);
            result.FilesCopied.Add("nvngx_package_config.txt");

            // Read version from config
            var content = File.ReadAllText(srcConfig);
            var match = Regex.Match(content, @"dlss,\s+([\d.]+)", RegexOptions.IgnoreCase);
            if (match.Success)
                result.AppliedVersion = match.Groups[1].Value;
        }

        progress?.Report(70);

        // Write nvngx_config.txt to activate override
 WriteNgXConfig(result.AppliedVersion);

        progress?.Report(100);

        result.Success = true;
        result.ConfigWritten = true;

        return result;
    }

    private static List<string> GetNgxCandidatePaths(string? ngxBasePath)
    {
        var candidates = new List<string>();

        // 1. Explicitly configured path (settings or parameter)
        if (!string.IsNullOrEmpty(ngxBasePath))
            candidates.Add(ngxBasePath);

        // 2. Default known paths, matching ScanService.ScanAllAsync behavior
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrEmpty(programData))
        {
            var programDataPath = Path.Combine(programData, "NVIDIA", "NGX");
            if (!candidates.Contains(programDataPath, StringComparer.OrdinalIgnoreCase))
                candidates.Add(programDataPath);
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            var appDataPath = Path.Combine(appData, "NVIDIA", "NGX");
            if (!candidates.Contains(appDataPath, StringComparer.OrdinalIgnoreCase))
                candidates.Add(appDataPath);
        }

        return candidates;
    }
    private string GetDlssVersionString()
    {
        var configPath = Path.Combine(InstallDir, "nvngx_package_config.txt");
        if (File.Exists(configPath))
        {
            try
            {
                var content = File.ReadAllText(configPath);
                var match = Regex.Match(content, @"dlss,\s+([\d.]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value;
            }
            catch { }
        }
        if (!string.IsNullOrEmpty(_dllVersion))
            return _dllVersion;
        return "310.6.0";
    }


    private void WriteNgXConfig(string? versionOverride = null)
    {
        try
        {
            var version = versionOverride ?? GetDlssVersionString();
            var ngxDir = Path.GetDirectoryName(ConfigFilePath);
            if (ngxDir != null && !Directory.Exists(ngxDir))
                Directory.CreateDirectory(ngxDir);

            var config = $@"[dlss_override]
app_E658700_force = 1
app_E658700 = {version}

[streamline_override]
app_E658703_force = 1
app_E658703 = {version}
";
            File.WriteAllText(ConfigFilePath, config);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Administrator access is required to activate the DLSS Override. " +
                "Restart the app as Administrator and try again.");
        }
    }

    private async Task<string?> GetLatestNvidiaReleaseAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/NVIDIA/DLSS/releases?per_page=1");
            request.Headers.Add("User-Agent", "DLSSVersionToolkit/2.0");
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var release = json.RootElement.EnumerateArray().FirstOrDefault();
            if (release.ValueKind == JsonValueKind.Undefined) return null;

            if (release.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.Equals("ngx_dlss_demo_windows.zip", StringComparison.OrdinalIgnoreCase))
                    {
                        return asset.GetProperty("browser_download_url").GetString();
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static string? ExtractVersionFromUrl(string url)
    {
        var match = Regex.Match(url, @"/releases/(?:download|tag)/v?([0-9.]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private async Task<string?> GetGlomDownloadUrl(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GlomRepoApi);
            request.Headers.Add("User-Agent", "DLSSVersionToolkit/2.0");
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) return null;

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            if (!json.RootElement.TryGetProperty("assets", out var assets))
                return null;

            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.StartsWith("nvidiaDlssGlom-v") && name.EndsWith(".rar"))
                {
                    return asset.GetProperty("browser_download_url").GetString();
                }
            }
        }
        catch { }

        return null;
    }

    private async Task<bool> DownloadFileAsync(string url, string destPath, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return false;

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

            var buffer = new byte[8192];
            long totalRead = 0;
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                totalRead += bytesRead;
            }

            return true;
        }
        catch { return false; }
    }

    private static Version? TryParseVersion(string version)
    {
        try
        {
            var cleaned = System.Text.RegularExpressions.Regex.Replace(version, "[a-zA-Z]", "");
            var parts = cleaned.Split('.');
            var major = int.TryParse(parts.ElementAtOrDefault(0), out var m) ? m : 0;
            var minor = int.TryParse(parts.ElementAtOrDefault(1), out var n) ? n : 0;
            var build = int.TryParse(parts.ElementAtOrDefault(2), out var b) ? b : 0;
            var rev = int.TryParse(parts.ElementAtOrDefault(3), out var r) ? r : 0;
            return new Version(major, minor, build, rev);
        }
        catch { return null; }
    }
}