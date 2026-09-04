namespace DLSSVersionToolkit.Core.Services;

using System.Net.Http;
using System.Text.RegularExpressions;

/// <summary>
/// A component version as published by NVIDIA's NGX OTA channel.
/// </summary>
public class OtaComponentVersion
{
    /// <summary>Manifest section name, e.g. "dlss", "dlssg", "sl_sdk_0".</summary>
    public string Component { get; set; } = "";

    /// <summary>Dotted version, e.g. "310.7.128".</summary>
    public string Version { get; set; } = "";
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
///   * this reads VERSION METADATA only. It does not download or install payloads. Pulling
///     executables from an undocumented endpoint into %ProgramData% is a supply-chain path, and
///     that decision is deliberately not taken here.
/// </summary>
public class NvidiaOtaService
{
    /// <summary>
    /// Production channel root. The channel GUIDs are opaque; this one served the newest
    /// *released* versions across every component when surveyed, and its sibling `dev-models`
    /// root ran ahead of it (310.9.0 / 2.14.0 while production was 310.7.128 / 2.12.128).
    ///
    /// Production is used deliberately. A staging channel is not "a newer version available to
    /// you" — telling a user their install is out of date against a build the driver will not
    /// serve them produces an update prompt that can never be satisfied.
    /// </summary>
    public const string ProductionChannel = "3e933c08-ea30-45ae-93d1-5114edf9c3b9";

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

    /// <summary>Cached manifest text, so a scan does not re-fetch per component.</summary>
    private string? _cachedManifest;
    private DateTime _cachedAt = DateTime.MinValue;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Fetches and parses the OTA manifest. Returns an empty list on any failure — offline, DNS
    /// blocked, endpoint withdrawn, shape changed. The caller must treat empty as "no OTA
    /// opinion", never as "nothing is available".
    /// </summary>
    public async Task<IReadOnlyList<OtaComponentVersion>> GetVersionsAsync(CancellationToken ct = default)
    {
        var text = await FetchManifestAsync(ct);
        return string.IsNullOrEmpty(text) ? Array.Empty<OtaComponentVersion>() : Parse(text);
    }

    /// <summary>Newest OTA version for a manifest section, or null when unavailable.</summary>
    public async Task<string?> GetComponentVersionAsync(string component, CancellationToken ct = default)
    {
        var versions = await GetVersionsAsync(ct);
        return versions.FirstOrDefault(v =>
            string.Equals(v.Component, component, StringComparison.OrdinalIgnoreCase))?.Version;
    }

    private async Task<string?> FetchManifestAsync(CancellationToken ct)
    {
        if (_cachedManifest != null && DateTime.UtcNow - _cachedAt < CacheLifetime)
            return _cachedManifest;

        try
        {
            var url = string.Format(ManifestUrlTemplate, ProductionChannel);
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // The manifest is served no-cache precisely because it is meant to be re-read.
            using var response = await _http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"NvidiaOtaService: manifest HTTP {(int)response.StatusCode}");
                return null;
            }

            var text = await response.Content.ReadAsStringAsync(ct);
            _cachedManifest = text;
            _cachedAt = DateTime.UtcNow;
            return text;
        }
        catch (Exception ex)
        {
            // Offline is the common case and is not an error worth surfacing.
            System.Diagnostics.Debug.WriteLine($"NvidiaOtaService: manifest fetch failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parses the INI-style manifest: one [section] per component, one app_&lt;CMSID&gt; = version
    /// line per registered application id. Only the generic app ids are reported — the per-title
    /// pins describe what one specific game gets, which is not "the latest available version".
    /// </summary>
    public static IReadOnlyList<OtaComponentVersion> Parse(string manifestText)
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
                results.Add(new OtaComponentVersion { Component = section, Version = value });
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
