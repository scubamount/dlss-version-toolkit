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
    private static readonly Regex GlomVersionRegex = new(
        @"nvidiaDlssGlom-v([0-9.]+)-",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    /// <summary>Extracts the release version from a nvidiaDlssGlom archive filename.</summary>
    public static string? ParseGlomVersionFromArchiveName(string fileName)
    {
        var match = GlomVersionRegex.Match(Path.GetFileName(fileName));
        return match.Success ? match.Groups[1].Value : null;
    }

    /// <summary>
    /// Orders AnWave cache archive paths by parsed release version, newest first. Creation
    /// timestamps describe when an archive entered the cache, not whether its release is newer.
    /// </summary>
    public static IEnumerable<string> OrderGlomArchivePathsNewestFirst(IEnumerable<string> paths) =>
        paths.OrderByDescending(path =>
            Version.TryParse(ParseGlomVersionFromArchiveName(path), out var version)
                ? version : new Version(0, 0));

    private void TrimGlomCache(int keepCount = 2)
    {
        if (!Directory.Exists(CacheDir)) return;

        var files = OrderGlomArchivePathsNewestFirst(Directory.GetFiles(CacheDir, "nvidiaDlssGlom*.rar"))
            .Select(f => new FileInfo(f))
            .Where(fi => fi.Exists)
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

        // Find the DLLs in the NGX Release folder. latestRelease.Path is the EXACT version
        // folder (NgxScanner sets Path = the per-version directory), so search only there and
        // its immediate children — NOT recursively into the whole versions/ tree, which would
        // let a sibling (older) version's DLL bleed in. That sibling-bleed was a root cause of
        // AnWave applying 310.6 right after NGX synced 310.7.
        var ngxDll = Directory.GetFiles(latestRelease.Path, "nvngx_dlss.dll", SearchOption.AllDirectories).FirstOrDefault();
        var ngxFolder = ngxDll != null ? Path.GetDirectoryName(ngxDll) : null;

        if (ngxFolder == null || !Directory.Exists(ngxFolder))
        {
            result.ErrorMessage = $"Could not locate NGX Release DLL folder under: {latestRelease.Path}";
            return result;
        }

        // Copy whichever NGX DLLs actually exist in the source folder. The set varies by source:
        // the NVIDIA/DLSS demo zip ships ONLY nvngx_dlss.dll, while the Streamline SDK ships all
        // of dlss/dlssg/dlssd/deepdvc. Copy what's present; never fail because an optional one is
        // absent. (Verified against v310.7.0 / Streamline v2.12.0 artifacts.)
        // Derives from UpgradeService.NgxDllNames (the canonical set) so this list cannot drift
        // from what UpgradeService syncs — v0.0.43's DeepDVC-missing bug was exactly that drift.
        var dllsToCopy = UpgradeService.NgxDllNames;

	foreach (var dllName in dllsToCopy)
	{
		var srcDll = Path.Combine(ngxFolder, dllName);
		if (File.Exists(srcDll))
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

        // Derive the applied version from the DLL we just copied — its FileVersionInfo is the
        // authoritative source. The SDK zips do NOT ship nvngx_package_config.txt, so the old
        // config-parse path always missed and fell back to a hardcoded "310.6.0". Read the real
        // version from nvngx_dlss.dll instead.
        result.AppliedVersion = DllVersionReader.ReadDlssVersionFromFolder(ngxFolder);

        // Copy the package config too IF the source happens to provide one (older NGX layouts do;
        // SDK zips do not). Never the version source anymore — purely a passthrough artifact.
        var srcConfig = Path.Combine(ngxFolder, "nvngx_package_config.txt");
        if (File.Exists(srcConfig))
        {
            var destConfig = Path.Combine(anWavePath, "nvngx_package_config.txt");
            File.Copy(srcConfig, destConfig, true);
            result.FilesCopied.Add("nvngx_package_config.txt");

            // Last-resort version source only if the DLL had no readable version resource.
            if (string.IsNullOrEmpty(result.AppliedVersion))
            {
                var content = File.ReadAllText(srcConfig);
                var match = Regex.Match(content, @"dlss,\s+([\d.]+)", RegexOptions.IgnoreCase);
                if (match.Success)
                    result.AppliedVersion = match.Groups[1].Value;
            }
        }

        // Final fallback: the scanned NGX entry's parsed version (from NgxScanner). Still never a
        // hardcoded literal.
        if (string.IsNullOrEmpty(result.AppliedVersion) && latestRelease.DLSS != "Unknown")
            result.AppliedVersion = latestRelease.DLSS;

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
        // Delegates to the shared resolver (v0.0.38): explicit → driver registry → defaults.
        return NgxPathResolver.GetCandidatePaths(ngxBasePath);
    }
    private string GetDlssVersionString()
    {
        // Authoritative source: the actual nvngx_dlss.dll in the AnWave install dir.
        var dllVersion = DllVersionReader.ReadDlssVersionFromFolder(InstallDir);
        if (!string.IsNullOrEmpty(dllVersion))
            return dllVersion;

        // Legacy fallback: a package config, if one was ever placed alongside.
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AnWaveAutoService: DLL version parse failed: {ex.Message}"); }
        }
        if (!string.IsNullOrEmpty(_dllVersion))
            return _dllVersion;

        // No version discoverable. Return empty rather than a hardcoded literal — a stale
        // hardcoded "310.6.0" here was the bug that wrote a wrong override after a 310.7 copy.
        return "";
    }


    private void WriteNgXConfig(string? versionOverride = null)
    {
        try
        {
            var version = versionOverride ?? GetDlssVersionString();
            // Normalize comma-form version resources ("310,7,0,0") to dotted form.
            if (!string.IsNullOrEmpty(version))
                version = version.Replace(',', '.').Trim();

            if (string.IsNullOrEmpty(version))
            {
                // No discoverable version — do NOT write a blank/garbage override line (the driver
                // would point at "" and silently ignore the override). Skip the write and surface
                // it so the caller can report a partial result instead of a false "applied".
                System.Diagnostics.Debug.WriteLine(
                    "WriteNgXConfig: no DLSS version discoverable; skipping override config write.");
                throw new InvalidOperationException(
                    "Could not determine the DLSS version to activate. The DLLs were copied but the " +
                    "override was not written. Re-run Update All, or use 'Sync NGX from DLSS'.");
            }

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
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AnWaveAutoService: ngx_dlss_demo URL fetch failed: {ex.Message}"); }

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
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"AnWaveAutoService: nvidiaDlssGlom URL fetch failed: {ex.Message}"); }

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