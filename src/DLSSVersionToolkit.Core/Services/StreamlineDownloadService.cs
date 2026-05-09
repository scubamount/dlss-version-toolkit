namespace DLSSVersionToolkit.Core.Services;

using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DLSSVersionToolkit.Core.Models;

public class StreamlineRelease
{
    public string Tag { get; set; } = "";
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public DateTime PublishedAt { get; set; }
    public string Body { get; set; } = "";
}

public interface IStreamlineDownloadService
{
    Task<List<StreamlineRelease>> GetAvailableReleasesAsync(CancellationToken ct = default);
    Task<string?> DownloadLatestAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    string? GetCachedDownloadPath();
    string? GetCachedSdkVersion();
    Task<UpgradeOperation?> SyncFromCachedSdkAsync(IProgress<int>? progress = null, CancellationToken ct = default);
    /// <summary>Returns count of cached downloads and total size in bytes.</summary>
    (int Count, long TotalBytes) GetCacheInfo();
    /// <summary>Removes all cached downloads older than the latest N, keeping the most recent keepCount versions.</summary>
    void TrimCache(int keepCount = 3);
}

public class StreamlineDownloadService : IStreamlineDownloadService
{
    private static readonly HttpClient _http;
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DLSSVersionToolkit", "StreamlineDownloads");

    static StreamlineDownloadService()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
    }

    private const string GitHubApiUrl = "https://api.github.com/repos/NVIDIA-RTX/Streamline/releases?per_page=10";
    // Asset naming pattern: streamline-sdk-v2.11.1.zip
    private const string AssetNamePrefix = "streamline-sdk-";

    private string? _cachedDownloadPath;

    public async Task<List<StreamlineRelease>> GetAvailableReleasesAsync(CancellationToken ct = default)
    {
        var releases = new List<StreamlineRelease>();

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
                        if (name.StartsWith(AssetNamePrefix, StringComparison.OrdinalIgnoreCase)
                            && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.TryGetProperty("browser_download_url", out var bdu)
                                ? bdu.GetString() ?? ""
                                : "";
                            break;
                        }
                    }
                }

                releases.Add(new StreamlineRelease
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
            Console.Error.WriteLine("Timeout fetching Streamline release list");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error fetching Streamline releases: {ex.Message}");
        }

        return releases;
    }

    public async Task<string?> DownloadLatestAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        var releases = await GetAvailableReleasesAsync(ct);
        var latest = releases.FirstOrDefault(r => !string.IsNullOrEmpty(r.DownloadUrl));
        if (latest == null || string.IsNullOrEmpty(latest.DownloadUrl))
            return null;

        if (!Directory.Exists(CacheDir))
            Directory.CreateDirectory(CacheDir);

        // Check if we already have this exact version cached
        var fileName = $"streamline-sdk-{latest.Version}.zip";
        var destPath = Path.Combine(CacheDir, fileName);
        if (File.Exists(destPath))
        {
            _cachedDownloadPath = destPath;
            return destPath;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, latest.DownloadUrl);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"Streamline download failed with status {response.StatusCode}");
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

            _cachedDownloadPath = destPath;
            Console.Error.WriteLine($"Streamline download complete: {destPath} ({totalRead} bytes)");

            // Housekeeping: trim old versions, keep only latest 3
            TrimCache(3);

            return destPath;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Access denied writing to Streamline cache directory: {ex.Message}");
            try { if (File.Exists(destPath + ".tmp")) File.Delete(destPath + ".tmp"); } catch { }
            return null;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"IO error during Streamline download: {ex.Message}");
            try { if (File.Exists(destPath + ".tmp")) File.Delete(destPath + ".tmp"); } catch { }
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Streamline download failed: {ex.Message}");
            try { if (File.Exists(destPath + ".tmp")) File.Delete(destPath + ".tmp"); } catch { }
            return null;
        }
    }

    public string? GetCachedDownloadPath() => _cachedDownloadPath;

    public string? GetCachedSdkVersion()
    {
        if (_cachedDownloadPath != null && File.Exists(_cachedDownloadPath))
        {
            var fileName = Path.GetFileName(_cachedDownloadPath); // e.g. "streamline-sdk-2.11.1.zip"
            if (fileName.StartsWith("streamline-sdk-") && fileName.EndsWith(".zip"))
            {
                return fileName.Substring(15, fileName.Length - 15 - 4); // "2.11.1"
            }
        }
        return null;
    }

    public Task<UpgradeOperation?> SyncFromCachedSdkAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (_cachedDownloadPath == null || !File.Exists(_cachedDownloadPath))
        {
            Console.Error.WriteLine("No cached Streamline SDK download found.");
            return Task.FromResult<UpgradeOperation?>(null);
        }

        var ngxBasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NVIDIA", "NGX");

        progress?.Report(0);

        // Extract the Streamline SDK zip to a temp directory, then sync to NGX
        var tempDir = Path.Combine(Path.GetTempPath(), $"DLSSVersionToolkit_Streamline_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            System.IO.Compression.ZipFile.ExtractToDirectory(_cachedDownloadPath, tempDir);

            // Find the extracted SDK folder — look for bin/x64/nvngx_dlss.dll
            var binPath = FindStreamlineBinPath(tempDir);
            if (binPath == null)
            {
                Console.Error.WriteLine("Could not find Streamline SDK DLLs inside the downloaded zip.");
                return Task.FromResult<UpgradeOperation?>(null);
            }

            var upgradeService = new UpgradeService(new NgxScanner(new NgxConfigParser()), new BackupService());
            var result = upgradeService.SyncToNGX(binPath, "StreamlineSDK", ngxBasePath);
            progress?.Report(100);
            return Task.FromResult<UpgradeOperation?>(result);
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static string? FindStreamlineBinPath(string rootDir)
    {
        // Try bin/x64/ first (standard Streamline SDK layout)
        var dllPath = Path.Combine(rootDir, "bin", "x64", "nvngx_dlss.dll");
        if (File.Exists(dllPath)) return Path.Combine(rootDir, "bin", "x64");

        // Try looking inside a subfolder (some releases have a top-level folder)
        foreach (var dir in Directory.GetDirectories(rootDir))
        {
            dllPath = Path.Combine(dir, "bin", "x64", "nvngx_dlss.dll");
            if (File.Exists(dllPath)) return Path.Combine(dir, "bin", "x64");
        }

        // Fall back: find any nvngx_dlss.dll anywhere in the extracted folder
        var found = Directory.GetFiles(rootDir, "nvngx_dlss.dll", SearchOption.AllDirectories).FirstOrDefault();
        if (found != null) return Path.GetDirectoryName(found);

        return null;
    }

    public (int Count, long TotalBytes) GetCacheInfo()
    {
        if (!Directory.Exists(CacheDir)) return (0, 0);
        var files = Directory.GetFiles(CacheDir, "streamline-sdk-*.zip").ToList();
        long total = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
        return (files.Count, total);
    }

    public void TrimCache(int keepCount = 3)
    {
        if (!Directory.Exists(CacheDir)) return;

        var files = Directory.GetFiles(CacheDir, "streamline-sdk-*.zip")
            .Select(f => new FileInfo(f))
            .Where(fi => fi.Exists)
            .OrderByDescending(fi => fi.CreationTimeUtc)
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
