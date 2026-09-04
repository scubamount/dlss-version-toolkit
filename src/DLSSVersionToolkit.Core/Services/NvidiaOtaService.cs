namespace DLSSVersionToolkit.Core.Services;

using System.Net.Http;
using System.Text.RegularExpressions;

/// <summary>
/// Which NVIDIA NGX channel a version came from.
///
/// NVIDIA's own Streamline OTA client (source/core/sl.ota/ota.cpp) treats this as a two-value
/// switch: registry `NGXCore\CDNServerType`, "0 - production / 1 - staging", selecting
/// `NVIDIA/NGX/models/` or `NVIDIA/NGX/Staging/models/`. This enum mirrors that.
/// </summary>
public enum OtaChannel
{
    /// <summary>What the driver serves a normal machine.</summary>
    Production,

    /// <summary>
    /// NVIDIA's staging root (`dev-models`). Runs ahead of production — real, published builds,
    /// but pre-release: the driver will not hand these to a game on its own.
    /// </summary>
    Staging,
}

/// <summary>
/// A component version as published by NVIDIA's NGX OTA channel.
/// </summary>
public class OtaComponentVersion
{
    /// <summary>Manifest section name, e.g. "dlss", "dlssg", "sl_sdk_0".</summary>
    public string Component { get; set; } = "";

    /// <summary>Dotted version, e.g. "310.7.128".</summary>
    public string Version { get; set; } = "";

    /// <summary>Which channel published this version.</summary>
    public OtaChannel Channel { get; set; } = OtaChannel.Production;

    /// <summary>True when this came from the staging channel and is therefore pre-release.</summary>
    public bool IsPreRelease => Channel == OtaChannel.Staging;
}

/// <summary>
/// Reads NVIDIA's NGX OTA version manifest — the channel the driver itself updates from.
///
/// WHY THIS EXISTS (v0.71). "LATEST AVAILABLE" was computed purely from GitHub releases
/// (NVIDIA/DLSS, NVIDIA-RTX/Streamline). Those publish the *SDK* — what a developer can build
/// against — and they lag what the driver actually loads: at the time this was written GitHub's
/// newest DLSS was 310.7.0 while the OTA production channel served 310.7.128, and Streamline was
/// 2.12.0 on GitHub versus 2.12.128 on OTA. So the app could show "UP TO DATE" against a number
/// that was not the newest thing NVIDIA ships.
///
/// PROVENANCE. This host is UNDOCUMENTED. It was found by inspecting NGX's own update path and
/// every claim here was verified by fetching it (2026-09-03): the manifest parses, the packed
/// version folders it implies resolve to real payloads, the published .sha256 sidecar matched a
/// downloaded 89 MB bundle byte-for-byte, and the DLL inside carried a PE FileVersion equal to
/// the manifest's version. NVIDIA can change or withdraw it without notice, so:
///   * every failure here is non-fatal and falls back to the GitHub feed;
///   * the UI labels which source a version came from rather than implying one authority;
///   * this class reads VERSION METADATA only. Payload downloading lives in
///     <see cref="OtaPayloadDownloader"/>, is opt-in, and verifies every byte before use.
/// </summary>
public class NvidiaOtaService
{
    /// <summary>
    /// Production channel root — what the driver serves a normal machine.
    ///
    /// This pick is VERIFIED, not inferred (v0.72). Two independent checks agree:
    ///   * NVIDIA's own Streamline OTA client (source/core/sl.ota/ota.cpp) documents the switch
    ///     as registry `NGXCore\CDNServerType`, "0 - production / 1 - staging" — so a staging
    ///     channel is a real, separate thing and not the default.
    ///   * Only this root serves the build a current machine actually runs: the 310.7.128 payload
    ///     (packed 20318080) returns HTTP 200 here and 404s on the `d6e9b45e…` root, which stops
    ///     at 310.6.0 and whose manifest has not been touched since 2026-03-19.
    ///
    /// Note for anyone comparing with dlss-swapper: it hardcodes the `d6e9b45e…` root with a
    /// FLATTER key layout (guid/dlss/versions/... — no org/nvidia/team/ngx/models/ segment).
    /// That path still resolves for older builds but is stale for current ones — do not copy it.
    /// </summary>
    public const string ProductionChannel = "3e933c08-ea30-45ae-93d1-5114edf9c3b9";

    /// <summary>
    /// Staging channel root. Runs ahead of production (310.9.0 / 2.14.0 while production served
    /// 310.7.128 / 2.12.128) and is refreshed far more often.
    ///
    /// These are real published builds, not fabrications — but the driver does not hand them to a
    /// game on its own, so a staging version is a PRE-RELEASE. It is surfaced only when it is
    /// strictly newer than every other feed, always labelled, and installing from it is opt-in.
    /// </summary>
    public const string StagingChannel = "dev-models";

    /// <summary>Root for a channel.</summary>
    public static string RootFor(OtaChannel channel) =>
        channel == OtaChannel.Staging ? StagingChannel : ProductionChannel;

    private const string ManifestUrlTemplate =
        "https://ngx.download.nvidia.com/{0}/org/nvidia/team/ngx/models/config/versions/2/files/nvngx_server_config.txt";

    /// <summary>
    /// The generic application ids. NGX resolves an unregistered title to one of these, so their
    /// value is the version a normal game gets — as opposed to the per-title `app_<CMSID>` pins
    /// that fill most of the manifest.
    /// </summary>
    private static readonly string[] GenericAppKeys = { "app_E658700", "app_E658703" };

    private readonly HttpClient _http;

    public NvidiaOtaService(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>Cached manifest text per channel, so a scan does not re-fetch per component.</summary>
    private readonly Dictionary<OtaChannel, (string Text, DateTime At)> _cache = new();
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Fetches and parses one channel's manifest. Returns an empty list on any failure — offline,
    /// DNS blocked, endpoint withdrawn, shape changed. The caller must treat empty as "no OTA
    /// opinion", never as "nothing is available".
    /// </summary>
    public async Task<IReadOnlyList<OtaComponentVersion>> GetVersionsAsync(
        OtaChannel channel = OtaChannel.Production, CancellationToken ct = default)
    {
        var text = await FetchManifestAsync(channel, ct);
        return string.IsNullOrEmpty(text)
            ? Array.Empty<OtaComponentVersion>()
            : Parse(text, channel);
    }

    /// <summary>Newest OTA version for a manifest section on one channel, or null when unavailable.</summary>
    public async Task<string?> GetComponentVersionAsync(
        string component, OtaChannel channel = OtaChannel.Production, CancellationToken ct = default)
    {
        var versions = await GetVersionsAsync(channel, ct);
        return versions.FirstOrDefault(v =>
            string.Equals(v.Component, component, StringComparison.OrdinalIgnoreCase))?.Version;
    }

    /// <summary>
    /// The newest version of a component across the channels the user has enabled, with the
    /// channel it came from.
    ///
    /// Staging only wins when it is STRICTLY newer — equal versions resolve to production, so a
    /// user never sees "pre-release" attached to a build that production also serves. When
    /// staging is not enabled this is exactly the production answer.
    /// </summary>
    public async Task<OtaComponentVersion?> GetNewestAsync(
        string component, bool includeStaging, CancellationToken ct = default)
    {
        OtaComponentVersion? best = null;

        foreach (var channel in includeStaging
                     ? new[] { OtaChannel.Production, OtaChannel.Staging }
                     : new[] { OtaChannel.Production })
        {
            var version = await GetComponentVersionAsync(component, channel, ct);
            if (string.IsNullOrEmpty(version))
                continue;

            // Strictly-greater keeps production authoritative on ties.
            if (best == null || CompareVersions(version, best.Version) > 0)
                best = new OtaComponentVersion
                {
                    Component = component,
                    Version = version,
                    Channel = channel,
                };
        }

        return best;
    }

    private async Task<string?> FetchManifestAsync(OtaChannel channel, CancellationToken ct)
    {
        if (_cache.TryGetValue(channel, out var hit) && DateTime.UtcNow - hit.At < CacheLifetime)
            return hit.Text;

        try
        {
            var url = string.Format(ManifestUrlTemplate, RootFor(channel));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // The manifest is served no-cache precisely because it is meant to be re-read.
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"NvidiaOtaService: {channel} manifest HTTP {(int)response.StatusCode}");
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(ct);
            _cache[channel] = (text, DateTime.UtcNow);
            return text;
        }
        catch (Exception ex)
        {
            // Offline is the common case and is not an error worth surfacing.
            System.Diagnostics.Debug.WriteLine(
                $"NvidiaOtaService: {channel} manifest fetch failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses the INI-style manifest: one [section] per component, one app_&lt;CMSID&gt; = version
    /// line per registered application id. Only the generic app ids are reported — the per-title
    /// pins describe what one specific game gets, which is not "the latest available version".
    /// </summary>
    public static IReadOnlyList<OtaComponentVersion> Parse(
        string manifestText, OtaChannel channel = OtaChannel.Production)
    {
        var results = new List<OtaComponentVersion>();
        if (string.IsNullOrWhiteSpace(manifestText))
            return results;

        string? section = null;
        foreach (var raw in manifestText.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1].Trim();
                continue;
            }

            if (section == null)
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            // Strip a trailing inline comment, which the manifest does use.
            var semi = value.IndexOf(';');
            if (semi >= 0)
                value = value[..semi].Trim();

            if (!GenericAppKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                continue;

            if (!DllVersionReader.IsValidVersion(value))
                continue;

            // A section can list both generic ids; keep the newest so the two never disagree.
            var existing = results.FirstOrDefault(r =>
                string.Equals(r.Component, section, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
                results.Add(new OtaComponentVersion
                {
                    Component = section,
                    Version = value,
                    Channel = channel,
                });
            else if (CompareVersions(value, existing.Version) > 0)
                existing.Version = value;
        }

        return results;
    }

    /// <summary>
    /// 4-part numeric comparison. Deliberately numeric, not lexical: "310.129.0" is NEWER than
    /// "310.9.0" but sorts BEFORE it as a string, and OTA build numbers reach three digits
    /// (310.7.128), so string ordering here would report upgrades as downgrades.
    /// </summary>
    public static int CompareVersions(string? a, string? b)
    {
        static int[] Parts(string? v) =>
            (v ?? "").Split('.')
                .Select(p => int.TryParse(Regex.Replace(p, "[^0-9]", ""), out var n) ? n : 0)
                .Concat(Enumerable.Repeat(0, 4))
                .Take(4)
                .ToArray();

        var pa = Parts(a);
        var pb = Parts(b);
        for (var i = 0; i < 4; i++)
        {
            if (pa[i] != pb[i])
                return pa[i].CompareTo(pb[i]);
        }
        return 0;
    }
}
