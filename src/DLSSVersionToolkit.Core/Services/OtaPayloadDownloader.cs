namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;
using System.Net.Http;
using System.Security.Cryptography;

/// <summary>Outcome of an OTA payload fetch.</summary>
public class OtaDownloadResult
{
    public bool Success { get; set; }

    /// <summary>Where the verified file landed. Null unless <see cref="Success"/>.</summary>
    public string? Path { get; set; }

    /// <summary>Human-readable reason for a failure, safe to show in the run report.</summary>
    public string? Error { get; set; }

    public string Component { get; set; } = "";
    public string Version { get; set; } = "";
    public OtaChannel Channel { get; set; } = OtaChannel.Production;

    /// <summary>SHA-256 of the accepted bytes, for the run report and the manifest record.</summary>
    public string? Sha256 { get; set; }
}

/// <summary>
/// Downloads NGX component payloads from NVIDIA's OTA CDN — the same files the driver's own
/// updater pulls.
///
/// WHY THIS IS OPT-IN AND VERIFIED (v0.72). This fetches NVIDIA-copyrighted binaries from an
/// UNDOCUMENTED endpoint and writes them into the NGX tree. That is a supply-chain path, so the
/// safety properties are not optional and are not "best effort":
///
///   1. HTTPS only. A plain-http URL is rejected before a request is made.
///   2. The published `.sha256` sidecar is fetched FIRST and the payload is accepted only if the
///      computed digest matches. No sidecar, no install — a missing sidecar is a failure, never
///      a skipped check. (Verified against the live CDN: the 310.7.128 DLSS payload's sidecar
///      matches its 74,208,880 bytes exactly.)
///   3. The bytes must be a PE image (`MZ`). A CDN error page that returns HTTP 200 is caught
///      here rather than being written to disk as a .dll.
///   4. On Windows the file's Authenticode signature is checked and the signer chain must lead
///      to NVIDIA. An unsigned or wrongly-signed payload is deleted, not quarantined.
///   5. Everything lands in a temp file and is moved into place only after all of the above
///      pass, so a failed verification cannot leave a partial DLL where a scan will find it.
///
/// Any failure is non-fatal: the caller falls back to the GitHub SDK path, which is unchanged
/// and remains the default source.
/// </summary>
public class OtaPayloadDownloader
{
    private const string PayloadUrlTemplate =
        "https://ngx.download.nvidia.com/{0}/org/nvidia/team/ngx/models/{1}/versions/{2}/files/{3}";

    /// <summary>
    /// The generic-application payload filename. NGX resolves an unregistered title to the
    /// generic app id, so this is the file a normal game would receive.
    /// </summary>
    public const string GenericPayloadFile = "160_E658700.bin";

    private readonly HttpClient _http;
    private readonly IAuthenticodeVerifier _authenticode;

    public OtaPayloadDownloader(HttpClient? http = null, IAuthenticodeVerifier? authenticode = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _authenticode = authenticode ?? new WindowsAuthenticodeVerifier();
    }

    /// <summary>
    /// Packed version folder: major &lt;&lt; 16 | minor &lt;&lt; 8 | patch. This is NGX's own
    /// layout, confirmed against live payload URLs.
    /// </summary>
    public static int PackVersion(string version)
    {
        var parts = (version ?? "").Split('.');
        int Part(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
        return (Part(0) << 16) | (Part(1) << 8) | Part(2);
    }

    public static string BuildPayloadUrl(OtaChannel channel, string component, string version,
        string fileName = GenericPayloadFile) =>
        string.Format(PayloadUrlTemplate, NvidiaOtaService.RootFor(channel), component,
            PackVersion(version), fileName);

    /// <summary>
    /// The single predicate governing whether any OTA payload may be fetched. Both the
    /// preference and the acceptance must hold.
    ///
    /// Two flags rather than one because they answer different questions. AllowOtaPayloadDownloads
    /// is "prefer this source", which a default may reasonably set. OtaRedistributionAccepted is
    /// "I accept what fetching NVIDIA-copyrighted bytes from an undocumented endpoint means",
    /// which no default may set on a user's behalf. Collapsing them would let a shipped default
    /// stand in for consent nobody gave.
    ///
    /// Every download path routes through here — DownloadAsync refuses if this is false, so a
    /// future caller cannot reach the network by forgetting the check.
    /// </summary>
    public static bool IsDownloadPermitted(AppSettings settings) =>
        settings is { AllowOtaPayloadDownloads: true, OtaRedistributionAccepted: true };

    /// <summary>
    /// Fetches one component payload and verifies it end to end. Returns a failed result rather
    /// than throwing — the caller treats this as "OTA had nothing usable" and uses GitHub.
    /// </summary>
    public async Task<OtaDownloadResult> DownloadAsync(
        string component,
        string version,
        string destinationPath,
        OtaChannel channel = OtaChannel.Production,
        AppSettings? settings = null,
        CancellationToken ct = default)
    {
        var result = new OtaDownloadResult
        {
            Component = component,
            Version = version,
            Channel = channel,
        };

        // Consent is enforced at the download path itself, not only at the call site. A caller
        // that omits settings gets a refusal, so the failure mode of forgetting the gate is
        // "no download", never "silent download".
        if (settings is null || !IsDownloadPermitted(settings))
        {
            result.Error = "OTA payload downloads are not permitted: "
                + "both the OTA download setting and the redistribution acceptance are required.";
            return result;
        }

        var url = BuildPayloadUrl(channel, component, version);

        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            result.Error = "Refusing a non-HTTPS payload URL.";
            return result;
        }

        var temp = destinationPath + ".otadownload";

        try
        {
            // 1. The expected digest, before the payload. If NVIDIA does not publish one for this
            //    build, there is nothing to verify against and the download does not happen.
            var expected = await FetchSha256Async(url + ".sha256", ct);
            if (string.IsNullOrEmpty(expected))
            {
                result.Error = "No published .sha256 for this payload — refusing to install unverified bytes.";
                return result;
            }

            // 2. Payload.
            using (var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                if (!response.IsSuccessStatusCode)
                {
                    result.Error = $"OTA payload HTTP {(int)response.StatusCode}.";
                    return result;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
                await using var src = await response.Content.ReadAsStreamAsync(ct);
                await using var dst = File.Create(temp);
                await src.CopyToAsync(dst, ct);
            }

            // 3. Digest must match the published sidecar.
            var actual = await ComputeSha256Async(temp, ct);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                result.Error = $"SHA-256 mismatch (expected {expected[..16]}…, got {actual[..16]}…).";
                SafeDelete(temp);
                return result;
            }

            // 4. Must actually be a PE image — catches an HTTP 200 error page.
            if (!await IsPeImageAsync(temp, ct))
            {
                result.Error = "Payload is not a PE image.";
                SafeDelete(temp);
                return result;
            }

            // 5. Authenticode, and the signer must be NVIDIA.
            var signature = _authenticode.Verify(temp);
            if (!signature.IsValid)
            {
                result.Error = $"Authenticode check failed: {signature.Detail}";
                SafeDelete(temp);
                return result;
            }

            File.Move(temp, destinationPath, overwrite: true);

            result.Success = true;
            result.Path = destinationPath;
            result.Sha256 = actual;
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            SafeDelete(temp);
            return result;
        }
    }

    private async Task<string?> FetchSha256Async(string url, CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = (await response.Content.ReadAsStringAsync(ct)).Trim();

            // The sidecar is a bare hex digest; tolerate "<hash>  <filename>" too.
            var token = body.Split(new[] { ' ', '\t', '\n', '\r', '=' },
                    StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(t => t.Length == 64 && t.All(Uri.IsHexDigit));

            return token;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static async Task<bool> IsPeImageAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var header = new byte[2];
        var read = await stream.ReadAsync(header.AsMemory(0, 2), ct);
        return read == 2 && header[0] == (byte)'M' && header[1] == (byte)'Z';
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best effort — a leftover .otadownload is inert and never scanned.
        }
    }
}
