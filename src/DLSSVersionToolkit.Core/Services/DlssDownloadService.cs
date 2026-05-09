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

    public async Task<List<DlssRelease>> GetAvailableReleasesAsync(CancellationToken ct = default)
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
        var releases = await GetAvailableReleasesAsync(ct);
        var latest = releases.FirstOrDefault();
        if (latest == null || string.IsNullOrEmpty(latest.DownloadUrl))
            return null;

        if (!Directory.Exists(CacheDir))
            Directory.CreateDirectory(CacheDir);

        var fileName = $"dlss-sdk-{latest.Version}.zip";
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

                _cachedDownloadPath = destPath;
                System.Console.Error.WriteLine($"Download complete: {destPath} ({totalRead} bytes)");
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

    public string? GetCachedDownloadPath() => _cachedDownloadPath;

    public string? GetCachedSdkVersion()
    {
        if (_cachedDownloadPath != null && File.Exists(_cachedDownloadPath))
        {
            var fileName = Path.GetFileName(_cachedDownloadPath); // e.g. "dlss-sdk-310.6.0.zip"
            if (fileName.StartsWith("dlss-sdk-") && fileName.EndsWith(".zip"))
            {
                return fileName.Substring(9, fileName.Length - 13 - 4); // "310.6.0"
            }
        }
        return null;
    }

    public Task<UpgradeOperation?> SyncFromCachedSdkAsync(IProgress<int>? progress = null, CancellationToken ct = default)
    {
        if (_cachedDownloadPath == null || !File.Exists(_cachedDownloadPath))
        {
            Console.Error.WriteLine("No cached SDK download found.");
            return Task.FromResult<UpgradeOperation?>(null);
        }

        var ngxBasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NVIDIA", "NGX");

        progress?.Report(0);
        var upgradeService = new UpgradeService(new NgxScanner(new NgxConfigParser()), new BackupService());
        var result = upgradeService.SyncFromDlssSDK(_cachedDownloadPath, ngxBasePath);
        progress?.Report(100);
        return Task.FromResult<UpgradeOperation?>(result);
    }
}