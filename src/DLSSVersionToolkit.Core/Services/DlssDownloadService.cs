namespace DLSSVersionToolkit.Core.Services;

using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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
                return null;

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

            if (File.Exists(destPath))
                File.Delete(destPath);
            File.Move(filePath, destPath);

            _cachedDownloadPath = destPath;
            return destPath;
        }
        catch
        {
            return null;
        }
    }

    public string? GetCachedDownloadPath() => _cachedDownloadPath;
}