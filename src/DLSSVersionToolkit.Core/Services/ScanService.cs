namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;

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

        var ngxPath = ngxBasePath ?? settings.NgxBasePath;
        var globalPath = string.IsNullOrEmpty(anWavePath) ? settings.AnWavePath : anWavePath;
        var slPath = string.IsNullOrEmpty(streamlinePath) ? settings.StreamlinePath : streamlinePath;

        // Auto-detect if paths are empty
        if (string.IsNullOrEmpty(globalPath))
        {
            globalPath = FindAnWaveInDownloads();
        }
        if (string.IsNullOrEmpty(slPath))
        {
            slPath = _streamlineScanner.AutoDetectInDownloads();
        }

        // Scan NGX
        var ngxEntries = _ngxScanner.Scan(ngxPath);
        if (ngxEntries.Count == 0)
        {
            result.Warnings.Add("No NGX versions found");
        }
        result.Sources.AddRange(ngxEntries);

        // Scan AnWave
        if (!string.IsNullOrEmpty(globalPath))
        {
            var globalEntry = _globalScanner.Scan(globalPath);
            if (globalEntry != null)
                result.Sources.Add(globalEntry);
            else
                result.Warnings.Add("AnWave/dlssglom not found or has no valid DLLs");
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
        catch { }

        return null;
    }
}