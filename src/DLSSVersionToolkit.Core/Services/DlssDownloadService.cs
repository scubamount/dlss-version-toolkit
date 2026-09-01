namespace DLSSVersionToolkit.Core.Services;

using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DLSSVersionToolkit.Core.Models;

public class DlssRelease
{
    public string Tag { get; set; } = "";
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public string Body { get; set; } = "";
}

public interface IDlssDownloadService
{
    Task<List<DlssRelease>> GetAvailableReleasesAsync(CancellationToken ct = default);
    Task<string?> DownloadLatestAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    string? GetCachedDownloadPath();
    string? GetCachedSdkVersion();
    Task<UpgradeOperation?> SyncFromCachedSdkAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    /// <summary>Returns count of cached downloads and total size in bytes.</summary>
    (int Count, long TotalBytes) GetCacheInfo();
    /// <summary>Removes all cached downloads older than the latest N, keeping the most recent keepCount versions.</summary>
    void TrimCache(int keepCount = 3);
}

public class DlssDownloadService : IDlssDownloadService
{
    private static readonly HttpClient _http;
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DLSSVersionToolkit", "Downloads");

    static DlssDownloadService()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
    }

    private const string GitHubApiUrl = "https://api.github.com/repos/NVIDIA/DLSS/releases?per_page=10";
    private const string AssetName = "ngx_dlss_demo_windows.zip";

    private string? _cachedDownloadPath;

    // Short-lived in-memory cache of the GitHub release list. ScanAsync runs on every app
    // launch (auto-scan) and previously hit the GitHub API each time — wasteful and prone to
    // rate-limiting / offline failures on the startup hot path. Cache the result for a TTL so
    // repeated scans within a session reuse it. Cleared implicitly when the process exits.
    private static readonly object _releaseCacheLock = new();
    private static List<DlssRelease>? _cachedReleases;
    private static DateTime _cachedReleasesAt = DateTime.MinValue;
    private static readonly TimeSpan ReleaseCacheTtl = TimeSpan.FromMinutes(30);

    public async Task<List<DlssRelease>> GetAvailableReleasesAsync(CancellationToken ct = default)
    {
        // Serve from cache when fresh (avoids a GitHub round-trip on every scan/launch).
        lock (_releaseCacheLock)
        {
            if (_cachedReleases is not null && DateTime.UtcNow - _cachedReleasesAt < ReleaseCacheTtl)
                return new List<DlssRelease>(_cachedReleases);
        }

        var releases = await FetchReleasesFromGitHubAsync(ct);

        // Only cache a non-empty success — an empty list means the call failed (offline /
        // rate-limited), and we don't want to pin that failure for the whole TTL.
        if (releases.Count > 0)
        {
            lock (_releaseCacheLock)
            {
                _cachedReleases = new List<DlssRelease>(releases);
                _cachedReleasesAt = DateTime.UtcNow;
            }
        }
        return releases;
    }

    private async Task<List<DlssRelease>> FetchReleasesFromGitHubAsync(CancellationToken ct = default)
    {
        var releases = new List<DlssRelease>();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubApiUrl);
            request.Headers.Add("User-Agent", "DLSSVersionToolkit/2.0");
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errBody = await response.Content.ReadAsStringAsync(ct);
                Console.Error.WriteLine($"GitHub API error {response.StatusCode}: {errBody}");
                return releases;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            foreach (var item in json.RootElement.EnumerateArray())
            {
                var tag = item.GetProperty("tag_name").GetString() ?? "";
                var publishedAtStr = item.TryGetProperty("published_at", out var pa) ? pa.GetString() : "";
                var body = item.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
                DateTime.TryParse(publishedAtStr, out var publishedAt);

                string? downloadUrl = null;
                if (item.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.Equals(AssetName, StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var bdu)
                                ? bdu.GetString() ?? ""
                                : "";
                            break;
                        }
                    }
                }

                releases.Add(new DlssRelease
                {
                    Tag = tag,
                    Version = tag.TrimStart('v'),
                    DownloadUrl = downloadUrl ?? "",
                    PublishedAt = publishedAt,
                    Body = body
                });
            }
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine("Timeout fetching release list");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error fetching releases: {ex.Message}");
        }

        return releases;
    }

    public async Task<string?> DownloadLatestAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
	// Pre-flight: network check before GitHub API call
	if (!OperationGuard.IsNetworkAvailable())
	{
		Console.Error.WriteLine("No network available for GitHub API call.");
		return null;
	}

	var releases = await GetAvailableReleasesAsync(ct);
	var latest = FindLatestDownloadableRelease(releases);
	if (latest == null)
		return null;

	if (!Directory.Exists(CacheDir))
		Directory.CreateDirectory(CacheDir);

	// Check if we already have this exact version cached
	var fileName = $"dlss-sdk-{latest.Version}.zip";
	var destPath = Path.Combine(CacheDir, fileName);
	if (File.Exists(destPath))
	{
		_cachedDownloadPath = destPath;
		return destPath;
	}

	// Pre-flight: disk space check before download (need ~200 MB for DLSS SDK zip)
	if (!OperationGuard.HasDiskSpace(CacheDir, 200 * 1024 * 1024))
	{
		Console.Error.WriteLine("Insufficient disk space for DLSS SDK download.");
		return null;
	}
        try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, latest.DownloadUrl);
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

                if (!response.IsSuccessStatusCode)
                {
                    System.Console.Error.WriteLine($"Download failed with status {response.StatusCode}");
                    return null;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var filePath = destPath + ".tmp";

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        var pct = (int)(totalRead * 100 / totalBytes);
                        progress?.Report(pct);
                    }
                }

                fileStream.Close();
                contentStream.Close();

		// Use Copy + Delete instead of Move (Move can fail across drives or with file locks)
		File.Copy(filePath, destPath, true);
		File.Delete(filePath);

		// Post-download verification: check file size matches
		if (!OperationGuard.VerifyFile(destPath, totalRead))
		{
			Console.Error.WriteLine($"Downloaded file verification failed: {destPath} (expected {totalRead} bytes)");
			try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
			return null;
		}

		_cachedDownloadPath = destPath;
		Console.Error.WriteLine($"Download complete: {destPath} ({totalRead} bytes)");

		// Housekeeping: trim old versions, keep only latest 3
		TrimCache(3);

		return destPath;
            }
            catch (UnauthorizedAccessException ex)
            {
                System.Console.Error.WriteLine($"Access denied writing to cache directory: {ex.Message}");
                try { if (File.Exists(destPath + ".tmp")) File.Delete(destPath + ".tmp"); } catch { }
                return null;
            }
            catch (IOException ex)
            {
                System.Console.Error.WriteLine($"IO error during download: {ex.Message}");
                try { if (File.Exists(destPath + ".tmp")) File.Delete(destPath + ".tmp"); } catch { }
                return null;
            }
            catch (Exception ex)
            {
                System.Console.Error.WriteLine($"Download failed: {ex.Message}");
                try { if (File.Exists(destPath + ".tmp")) File.Delete(destPath + ".tmp"); } catch { }
                return null;
            }
    }

    /// <summary>
    /// Returns the first release that actually publishes the required SDK archive. GitHub returns
    /// releases newest-first, but draft, metadata-only, or assetless releases must not block a
    /// downloadable release later in the list.
    /// </summary>
    public static DlssRelease? FindLatestDownloadableRelease(IEnumerable<DlssRelease> releases) =>
        releases.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.DownloadUrl));

    public string? GetCachedDownloadPath() => ResolveNewestCachedZip();

    /// <summary>Parses "dlss-sdk-310.7.0.zip" → "310.7.0"; null when not a cache zip name.</summary>
    public static string? ParseVersionFromZipName(string fileName)
    {
        if (!fileName.StartsWith("dlss-sdk-", StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return null;
        return fileName.Substring(9, fileName.Length - 9 - 4);
    }

    /// <summary>
    /// Session download first, else newest dlss-sdk-*.zip on disk by PARSED version.
    /// v0.0.43: same restart-amnesia bug class fixed for Streamline in v0.0.40 — the DLSS
    /// side still forgot its cache on every app restart (offline fallback and the pill's
    /// cached-version comparison both saw "no cache" despite the zip sitting on disk).
    /// </summary>
    private string? ResolveNewestCachedZip()
    {
        if (_cachedDownloadPath != null && File.Exists(_cachedDownloadPath))
            return _cachedDownloadPath;
        if (!Directory.Exists(CacheDir)) return null;
        return Directory.GetFiles(CacheDir, "dlss-sdk-*.zip")
            .OrderByDescending(f =>
                Version.TryParse(ParseVersionFromZipName(Path.GetFileName(f)) ?? "", out var v)
                    ? v : new Version(0, 0))
            .FirstOrDefault();
    }

    public string? GetCachedSdkVersion()
    {
        var zip = ResolveNewestCachedZip();
        return zip != null ? ParseVersionFromZipName(Path.GetFileName(zip)) : null;
    }

    public Task<UpgradeOperation?> SyncFromCachedSdkAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var zipPath = ResolveNewestCachedZip();
        if (zipPath == null)
        {
            Console.Error.WriteLine("No cached SDK download found.");
            return Task.FromResult<UpgradeOperation?>(null);
        }
        _cachedDownloadPath = zipPath;

        // One write-root rule for the whole app. A local ProgramData literal here ignored an
        // AppData-based NGX tree and any configured path — five copies of this existed.
        var ngxBasePath = NgxPathResolver.GetWritableBase(null);
        if (string.IsNullOrEmpty(ngxBasePath))
            return Task.FromResult<UpgradeOperation?>(null);

        progress?.Report(0);
        var upgradeService = new UpgradeService(new NgxScanner(new NgxConfigParser()), new BackupService(), new VersionComparer());
        var result = upgradeService.SyncFromDlssSDK(_cachedDownloadPath, ngxBasePath);
        progress?.Report(100);
        return Task.FromResult<UpgradeOperation?>(result);
    }

    public (int Count, long TotalBytes) GetCacheInfo()
    {
        if (!Directory.Exists(CacheDir)) return (0, 0);
        var files = Directory.GetFiles(CacheDir, "dlss-sdk-*.zip").ToList();
        long total = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        return (files.Count, total);
    }

    /// <summary>
    /// Orders DLSS cache archive paths by parsed SDK version, newest first. Creation timestamps
    /// describe when an archive was copied into the cache, not whether its SDK is newer.
    /// </summary>
    public static IEnumerable<string> OrderCachedZipPathsNewestFirst(IEnumerable<string> paths) =>
        paths.OrderByDescending(path =>
            Version.TryParse(ParseVersionFromZipName(Path.GetFileName(path)) ?? "", out var version)
                ? version : new Version(0, 0));

    public void TrimCache(int keepCount = 3)
    {
        if (!Directory.Exists(CacheDir)) return;

        var files = OrderCachedZipPathsNewestFirst(Directory.GetFiles(CacheDir, "dlss-sdk-*.zip"))
            .Select(f => new FileInfo(f))
            .Where(fi => fi.Exists)
            .ToList();

        foreach (var file in files.Skip(keepCount))
        {
            try
            {
                if (file.FullName != _cachedDownloadPath) // never delete active cache
                    File.Delete(file.FullName);
            }
            catch { }
        }
    }

}
