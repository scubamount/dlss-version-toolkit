namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;

using System.Diagnostics;
public interface IScanService
{
	Task<ScanResult> ScanAllAsync(string? ngxBasePath = null, string? anWavePath = null, string? streamlinePath = null);
}

public class ScanService : IScanService
{
    private readonly INgxScanner _ngxScanner;
    private readonly IGlobalScanner _globalScanner;
    private readonly IStreamlineScanner _streamlineScanner;
    private readonly IVersionComparer _versionComparer;
    private readonly ISettingsService _settingsService;
    private readonly OtaCacheScanner _otaCacheScanner;

    public ScanService(
        INgxScanner ngxScanner,
        IGlobalScanner globalScanner,
        IStreamlineScanner streamlineScanner,
        IVersionComparer versionComparer,
        ISettingsService settingsService,
        OtaCacheScanner? otaCacheScanner = null)
    {
        _ngxScanner = ngxScanner;
        _globalScanner = globalScanner;
        _streamlineScanner = streamlineScanner;
        _versionComparer = versionComparer;
        _settingsService = settingsService;
        // Optional so the existing test constructions keep compiling; it is stateless and
        // read-only, so a default instance is always correct rather than merely convenient.
        _otaCacheScanner = otaCacheScanner ?? new OtaCacheScanner();
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

        // Scan each NGX path — deduplicate by source name (first found wins).
        // v0.0.57: scan failures land in result.Errors instead of dying in Debug.WriteLine;
        // an empty grid must never be the only symptom.
        var scanErrors = new List<string>();
        foreach (var path in OrderForRowScan(ngxCandidates, NgxPathResolver.GetWritableBase(explicitNgxPath)))
        {
            var entries = _ngxScanner.Scan(path, scanErrors);
            foreach (var entry in entries)
            {
                if (!result.Sources.Any(s => s.Source == entry.Source))
                    result.Sources.Add(entry);
            }
        }
        // Harvest what NVIDIA's OWN updater already downloaded (v0.75). Same candidate roots, a
        // sibling subtree: models\<component>\versions\<packed>\files\*.bin, which the loop above
        // is structurally blind to because NgxScanner only walks models\dlss_override\versions.
        //
        // Deliberately NOT deduplicated by Source the way the loop above is. That dedup means
        // "one row per source name", which is right when a source contributes a single row —
        // but the OTA cache holds MANY versions under one source tag, and per-source dedup would
        // silently keep the first and discard the rest. Dedup here is by (Source, BuildID).
        foreach (var path in ngxCandidates)
        {
            var harvested = _otaCacheScanner.ToEntries(_otaCacheScanner.Harvest(path, scanErrors));
            foreach (var entry in harvested)
            {
                if (!result.Sources.Any(s =>
                        s.Source == entry.Source && s.BuildID == entry.BuildID))
                    result.Sources.Add(entry);
            }
        }

        foreach (var e in scanErrors)
        {
            if (!result.Errors.Contains(e))
                result.Errors.Add(e);
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
    /// Scan order for the one-row-per-source NGX loop: the root the app WRITES comes first, then
    /// the remaining candidates in their original order (v0.76).
    ///
    /// The loop keeps the first NGX_Release / NGX_Staging row it finds. v0.74 put the registry's
    /// OTACachePath at the head of the candidate list; where that differs from the write root, the
    /// grid's NGX Release row described a tree Update All never touches, so a successful update
    /// looked like it had changed nothing. Reading the written root first makes the row reflect
    /// what was just written.
    /// </summary>
    public static List<string> OrderForRowScan(IReadOnlyList<string> candidates, string? writableBase)
    {
        var ordered = new List<string>();
        if (!string.IsNullOrWhiteSpace(writableBase))
            ordered.Add(writableBase);
        foreach (var c in candidates)
        {
            if (!ordered.Any(o => string.Equals(
                    o.TrimEnd('\\', '/'), c.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
                ordered.Add(c);
        }
        return ordered;
    }
}