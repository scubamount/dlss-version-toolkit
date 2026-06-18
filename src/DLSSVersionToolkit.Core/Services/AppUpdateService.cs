namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using DLSSVersionToolkit.Core.Models;

/// <summary>
/// Checks scubamount/dlss-version-toolkit GitHub releases for a newer app version and
/// applies the update by swapping the running single-file exe.
///
/// Swap strategy (Windows): a running exe can be RENAMED but not deleted/overwritten.
/// 1. Download new exe to %AppData%\DLSSVersionToolkit\update\DLSSVersionToolkit.exe.new
/// 2. Rename running exe  -> DLSSVersionToolkit.exe.old   (same directory)
/// 3. Move .new           -> DLSSVersionToolkit.exe
/// 4. Restart; the new instance deletes the .old file on startup (CleanupAfterUpdate).
/// A crash between steps 2 and 3 leaves a launchable .old next to the missing exe, and the
/// rollback path restores it, so the install can never be bricked.
/// </summary>
public class AppUpdateService
{
    private static readonly HttpClient _http;

    static AppUpdateService()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(120) };
    }

    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/scubamount/dlss-version-toolkit/releases/latest";
    private const string ExeAssetName = "DLSSVersionToolkit.exe";

    /// <summary>Filename suffix of the checksum asset published alongside the exe.
    /// The full file is "DLSSVersionToolkit.exe.sha256" (sha256sum format: "hash  filename").</summary>
    private const string Sha256AssetSuffix = ".sha256";

    /// <summary>Manual download fallback shown in error messages when the swap fails.</summary>
    public const string ReleasesPageUrl = "https://github.com/scubamount/dlss-version-toolkit/releases/latest";

    private static readonly string UpdateStagingDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DLSSVersionToolkit", "update");

    /// <summary>
    /// The running app's version, read from the entry assembly. Falls back to 0.0.0.
    /// </summary>
    public static Version GetCurrentVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version;
        return v ?? new Version(0, 0, 0, 0);
    }

    /// <summary>
    /// Parses a release tag like "v0.0.31" or "0.0.31" into a Version. Returns null when
    /// the tag is not a parseable dotted version.
    /// </summary>
    public static Version? ParseTagVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim().TrimStart('v', 'V');
        // Version.TryParse requires at least major.minor
        if (!s.Contains('.')) return null;
        return Version.TryParse(s, out var v) ? v : null;
    }

    /// <summary>
    /// Renders a version for display, dropping trailing zero components so 0.0.35.0 → "0.0.35"
    /// but 0.0.35.1 → "0.0.35.1". Replaces ToString(3) which silently truncated the 4th component
    /// (decimal patches like 0.0.35.1 displayed as "0.0.35").
    /// </summary>
    public static string ToDisplayVersion(Version v)
    {
        var parts = new[] { Math.Max(v.Major, 0), Math.Max(v.Minor, 0),
                            Math.Max(v.Build, 0), Math.Max(v.Revision, 0) };
        var len = 4;
        while (len > 2 && parts[len - 1] == 0) len--;
        return string.Join(".", parts.Take(len));
    }

    /// <summary>
    /// True when <paramref name="latest"/> is strictly newer than <paramref name="current"/>.
    /// Normalizes undefined components (-1) to 0 so 0.0.31 vs 0.0.31.0 compares equal.
    /// </summary>
    public static bool IsNewer(Version? latest, Version current)
    {
        if (latest == null) return false;
        static Version Pad(Version v) => new(
            Math.Max(v.Major, 0), Math.Max(v.Minor, 0),
            Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        return Pad(latest) > Pad(current);
    }

    /// <summary>
    /// Queries GitHub for the latest release. Network/API failures return a "no update"
    /// result instead of throwing — the caller treats this as best-effort.
    /// </summary>
    public async Task<AppUpdateInfo> CheckForUpdateAsync(CancellationToken ct = default)
    {
        var current = GetCurrentVersion();
        var none = new AppUpdateInfo { CurrentVersion = ToDisplayVersion(current) };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
            request.Headers.Add("User-Agent", "DLSSVersionToolkit/2.0");
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                Debug.WriteLine($"AppUpdateService: GitHub API {response.StatusCode}");
                return none;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = json.RootElement;

            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            var latest = ParseTagVersion(tag);
            if (latest == null) return none;

            string? downloadUrl = null;
            string? sha256Url = null;
            long assetSize = 0;
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    if (name.Equals(ExeAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.TryGetProperty("browser_download_url", out var bdu)
                            ? bdu.GetString() : null;
                        assetSize = asset.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                    }
                    else if (name.EndsWith(Sha256AssetSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        sha256Url = asset.TryGetProperty("browser_download_url", out var bdu)
                            ? bdu.GetString() : null;
                    }
                }
            }

            var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";

            return new AppUpdateInfo
            {
                CurrentVersion = ToDisplayVersion(current),
                LatestVersion = ToDisplayVersion(latest),
                IsUpdateAvailable = IsNewer(latest, current) && !string.IsNullOrEmpty(downloadUrl),
                DownloadUrl = downloadUrl ?? "",
                AssetSize = assetSize,
                Sha256Url = sha256Url ?? "",
                ReleaseNotes = notes,
            };
        }
        catch (TaskCanceledException)
        {
            Debug.WriteLine("AppUpdateService: timeout checking for update");
            return none;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AppUpdateService: update check failed: {ex.Message}");
            return none;
        }
    }

    /// <summary>
    /// Downloads the new exe and swaps it in place of the running one.
    /// Returns the result; on success the caller should prompt for a restart.
    /// </summary>
    public async Task<AppUpdateResult> DownloadAndApplyAsync(
        AppUpdateInfo update,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        if (!update.IsUpdateAvailable || string.IsNullOrEmpty(update.DownloadUrl))
            return AppUpdateResult.Failed("No update is available to apply.");

        var currentExePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(currentExePath) || !File.Exists(currentExePath))
            return AppUpdateResult.Failed(
                "Could not determine the running executable path.\n\n" +
                $"What to do: download the new version manually from {ReleasesPageUrl}");

        // 1. Download to staging
        Directory.CreateDirectory(UpdateStagingDirectory);
        var stagedPath = Path.Combine(UpdateStagingDirectory, ExeAssetName + ".new");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, update.DownloadUrl);
            request.Headers.Add("User-Agent", "DLSSVersionToolkit/2.0");
            using var response = await _http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return AppUpdateResult.Failed(
                    $"Download failed: HTTP {(int)response.StatusCode}.\n\n" +
                    $"What to do: try again later or download manually from {ReleasesPageUrl}");

            var totalBytes = response.Content.Headers.ContentLength ?? update.AssetSize;
            await using (var src = await response.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(stagedPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long readTotal = 0;
                int read;
                while ((read = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await dst.WriteAsync(buffer.AsMemory(0, read), ct);
                    readTotal += read;
                    if (totalBytes > 0)
                        progress?.Report((int)(readTotal * 100 / totalBytes));
                }
            }

            // Integrity: size must match the release asset exactly (when known)
            var downloadedSize = new FileInfo(stagedPath).Length;
            if (update.AssetSize > 0 && downloadedSize != update.AssetSize)
            {
                File.Delete(stagedPath);
                return AppUpdateResult.Failed(
                    $"Downloaded file size ({downloadedSize:N0} bytes) does not match the " +
                    $"release asset ({update.AssetSize:N0} bytes) — the download may have been " +
                    "interrupted.\n\nWhat to do: try again.");
            }

            // Integrity: SHA256 hash verification (when a checksum asset was published).
            // This is the real integrity gate — size-matching alone is not a security control.
            // MITM or a corrupted download produces a different hash → update is refused.
            if (!string.IsNullOrEmpty(update.Sha256Url))
            {
                string expectedHash;
                try
                {
                    using var hashReq = new HttpRequestMessage(HttpMethod.Get, update.Sha256Url);
                    hashReq.Headers.Add("User-Agent", "DLSSVersionToolkit/2.0");
                    using var hashResp = await _http.SendAsync(hashReq, ct);
                    hashResp.EnsureSuccessStatusCode();
                    // sha256sum format: "<64-hex>  <filename>" — take the first whitespace-delimited token
                    var raw = await hashResp.Content.ReadAsStringAsync(ct);
                    expectedHash = raw.Trim().Split(' ', 2, StringSplitOptions.TrimEntries)[0];
                }
                catch (Exception ex)
                {
                    TryDelete(stagedPath);
                    return AppUpdateResult.Failed(
                        $"Could not download the SHA256 checksum: {ex.Message}\n\n" +
                        "The update was cancelled for safety.\n\n" +
                        $"What to do: try again, or download manually from {ReleasesPageUrl}");
                }

                string actualHash;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                await using (var stream = File.OpenRead(stagedPath))
                {
                    actualHash = BitConverter.ToString(await sha.ComputeHashAsync(stream, ct))
                        .Replace("-", "").ToLowerInvariant();
                }

                if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
                {
                    TryDelete(stagedPath);
                    return AppUpdateResult.Failed(
                        "SHA256 mismatch — the downloaded file may be corrupted or tampered with.\n\n" +
                        $"Expected: {expectedHash}\nActual:   {actualHash}\n\n" +
                        "The update was cancelled.\n\n" +
                        $"What to do: try again, or download manually from {ReleasesPageUrl}");
                }
            }
        }
        catch (TaskCanceledException)
        {
            TryDelete(stagedPath);
            return AppUpdateResult.Failed("Download timed out.\n\nWhat to do: try again.");
        }
        catch (Exception ex)
        {
            TryDelete(stagedPath);
            return AppUpdateResult.Failed(
                $"Download failed: {ex.Message}\n\n" +
                $"What to do: download manually from {ReleasesPageUrl}");
        }

        // 2-3. Swap. Rename running exe out of the way, then move the new one in.
        var oldPath = currentExePath + ".old";
        try
        {
            File.Move(currentExePath, oldPath, overwrite: true);
        }
        catch (Exception ex)
        {
            TryDelete(stagedPath);
            return AppUpdateResult.Failed(
                $"Could not rename the running executable: {ex.Message}\n\n" +
                "This usually means the folder requires Administrator access " +
                "(e.g. Program Files).\n\n" +
                $"What to do: restart the app as Administrator and try again, or download " +
                $"manually from {ReleasesPageUrl}");
        }

        try
        {
            File.Move(stagedPath, currentExePath);
        }
        catch (Exception ex)
        {
            // Rollback: put the original back so the install stays launchable.
            try { File.Move(oldPath, currentExePath); }
            catch { /* .old remains; still recoverable manually */ }
            TryDelete(stagedPath);
            return AppUpdateResult.Failed(
                $"Could not move the new executable into place: {ex.Message}\n\n" +
                "The previous version has been restored.\n\n" +
                $"What to do: download manually from {ReleasesPageUrl}");
        }

        return AppUpdateResult.Succeeded(currentExePath);
    }

    /// <summary>
    /// Restarts the app: launches the (new) exe with --wait-for-pid so the child waits for
    /// this process to release the single-instance mutex, then shuts the current one down
    /// via the supplied action (Application.Shutdown in WPF).
    /// </summary>
    public static void RestartForUpdate(string exePath, Action shutdown)
    {
        var pid = Environment.ProcessId;
        var psi = new ProcessStartInfo
        {
            FileName = exePath,
            Arguments = $"--wait-for-pid {pid}",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? "",
        };
        Process.Start(psi);
        shutdown();
    }

    /// <summary>
    /// Startup hygiene: delete the previous version's .old file and the staging dir.
    /// Best-effort and silent — the .old may still be locked briefly after a restart.
    /// </summary>
    public static void CleanupAfterUpdate()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var old = exe + ".old";
                if (File.Exists(old))
                {
                    // Retry a few times — the old process may not have fully exited.
                    for (var i = 0; i < 5; i++)
                    {
                        try { File.Delete(old); break; }
                        catch (IOException) { Thread.Sleep(300); }
                        catch (UnauthorizedAccessException) { Thread.Sleep(300); }
                    }
                }
            }
            if (Directory.Exists(UpdateStagingDirectory))
                Directory.Delete(UpdateStagingDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AppUpdateService: cleanup skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles the --wait-for-pid launch argument: blocks (max 10s) until the given process
    /// exits, so the new instance doesn't lose the single-instance mutex race to the old one.
    /// Call BEFORE the mutex check during startup.
    /// </summary>
    public static void WaitForPredecessorIfRequested(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (!args[i].Equals("--wait-for-pid", StringComparison.OrdinalIgnoreCase)) continue;
            if (!int.TryParse(args[i + 1], out var pid)) return;
            try
            {
                using var proc = Process.GetProcessById(pid);
                proc.WaitForExit(10_000);
            }
            catch (ArgumentException)
            {
                // Process already gone — nothing to wait for.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppUpdateService: wait-for-pid skipped: {ex.Message}");
            }
            return;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* best effort */ }
    }
}
