namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;

using System.Diagnostics;
public interface IScanService
{
	Task<ScanResult> ScanAllAsync(string? ngxBasePath = null, string? anWavePath = null, string? streamlinePath = null);
	/// <summary>Verifies that a DLL file has a valid MZ/PE header signature.</summary>
	bool VerifyDllIntegrity(string dllPath);
}

public class ScanService : IScanService
{
    private readonly INgxScanner _ngxScanner;
    private readonly IGlobalScanner _globalScanner;
    private readonly IStreamlineScanner _streamlineScanner;
    private readonly IVersionComparer _versionComparer;
    private readonly ISettingsService _settingsService;

    public ScanService(
        INgxScanner ngxScanner,
        IGlobalScanner globalScanner,
        IStreamlineScanner streamlineScanner,
        IVersionComparer versionComparer,
        ISettingsService settingsService)
    {
        _ngxScanner = ngxScanner;
        _globalScanner = globalScanner;
        _streamlineScanner = streamlineScanner;
        _versionComparer = versionComparer;
        _settingsService = settingsService;
    }

    public async Task<ScanResult> ScanAllAsync(string? ngxBasePath = null, string? anWavePath = null, string? streamlinePath = null)
    {
        var start = DateTime.UtcNow;
        var result = new ScanResult();

        var settings = await _settingsService.LoadAsync();

        var explicitNgxPath = ngxBasePath ?? settings.NgxBasePath;
	var globalPath = string.IsNullOrEmpty(anWavePath) ? settings.AnWavePath : anWavePath;
	var slPath = string.IsNullOrEmpty(streamlinePath) ? settings.StreamlinePath : streamlinePath;

	// If configured paths don't exist on disk, clear them so auto-detect can find real paths
	if (!string.IsNullOrEmpty(globalPath) && !Directory.Exists(globalPath))
		globalPath = null;
	if (!string.IsNullOrEmpty(slPath) && !Directory.Exists(slPath))
		slPath = null;

        // Collect all NGX base paths to scan: explicit (settings/param) → driver registry
        // (HKLM\SOFTWARE\NVIDIA Corporation\Global\NGXCore|NGX) → default filesystem paths.
        // Centralized in NgxPathResolver (v0.0.38) so scan/sync/AnWave probe identically.
        var ngxCandidates = NgxPathResolver.GetCandidatePaths(explicitNgxPath);

        result.NgxPathsChecked = ngxCandidates;

        // Scan each NGX path — deduplicate by source name (first found wins)
        foreach (var path in ngxCandidates)
        {
            var entries = _ngxScanner.Scan(path);
            foreach (var entry in entries)
            {
                if (!result.Sources.Any(s => s.Source == entry.Source))
                    result.Sources.Add(entry);
            }
        }

        if (result.Sources.Count == 0)
        {
            result.Warnings.Add("No NGX versions found at any known path");
        }

	// Auto-detect AnWave path if not configured (v0.0.38: probe chain instead of a single
	// hardcoded dir). Order: toolkit's own install dir → user's Downloads folder (manual
	// AnWave/nvidiaDlssGlom unpack). FindAnWaveInDownloads existed since the AnWave feature
	// landed but was never wired into the chain — dead code until now.
	if (string.IsNullOrEmpty(globalPath))
	{
		var anWaveAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		if (!string.IsNullOrEmpty(anWaveAppData))
		{
			var defaultAnWave = Path.Combine(anWaveAppData, "DLSSVersionToolkit", "AnWave");
			if (Directory.Exists(defaultAnWave))
				globalPath = defaultAnWave;
		}

		if (string.IsNullOrEmpty(globalPath))
			globalPath = FindAnWaveInDownloads();
	}

	// Scan AnWave
	if (!string.IsNullOrEmpty(globalPath))
	{
		var globalEntry = _globalScanner.Scan(globalPath);
		if (globalEntry != null)
			result.Sources.Add(globalEntry);
		else
			result.Warnings.Add("AnWave/dlssglom not found or has no valid DLLs");
	}

	// Auto-detect Streamline SDK path if not configured
	if (string.IsNullOrEmpty(slPath))
	{
		var detected = _streamlineScanner.AutoDetectInDownloads();
		if (!string.IsNullOrEmpty(detected))
			slPath = detected;
	}

	// Scan Streamline SDK
	if (!string.IsNullOrEmpty(slPath))
	{
		var slEntry = _streamlineScanner.Scan(slPath);
		if (slEntry != null)
			result.Sources.Add(slEntry);
		else
			result.Warnings.Add("Streamline SDK not found at specified path");
	}

        // Mark newest versions
        _versionComparer.MarkNewest(result);

        // Generate recommendations
        result.Recommendations = _versionComparer.GenerateRecommendations(result);

        result.ScannedAt = DateTime.UtcNow;
        result.Duration = DateTime.UtcNow - start;

        return result;
    }

    private static string? FindAnWaveInDownloads()
    {
        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(downloads)) return null;
        var downloadsPath = Path.Combine(downloads, "Downloads");

        if (!Directory.Exists(downloadsPath))
            return null;

        try
        {
            var candidates = Directory.GetDirectories(downloadsPath)
                .Where(d => System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(d), "dlssglom|nvidiaDlssGlom|AnWave",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .ToList();

            foreach (var candidate in candidates)
            {
                var exePath = Path.Combine(candidate, "nvidiaDlssGlom.exe");
                if (File.Exists(exePath))
                    return candidate;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"FindAnWaveInDownloads: error scanning downloads directory: {ex.Message}");
        }

        return null;
    }
	/// <summary>
	/// Verifies that a DLL file has a valid MZ/PE header signature.
	/// Delegates to OperationGuard.VerifyDllSignature.
	/// </summary>
	public bool VerifyDllIntegrity(string dllPath)
	{
		return OperationGuard.VerifyDllSignature(dllPath);
	}
}