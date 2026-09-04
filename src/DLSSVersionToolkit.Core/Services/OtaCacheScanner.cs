namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Reads the components NVIDIA's own OTA updater has ALREADY downloaded to this machine (v0.75).
///
/// The toolkit writes its own payloads into <c>models\dlss_override\versions\&lt;dotted&gt;\</c> and
/// <c>models\&lt;component&gt;\versions\&lt;packed&gt;\files\&lt;arch&gt;_&lt;appId&gt;.bin</c>. NVIDIA's
/// updater (<c>nvngx_update.exe</c>, invoked by the driver when a game initializes NGX) populates
/// that SECOND tree itself, for its own components. <see cref="NgxScanner"/> only ever looked at
/// <c>dlss_override</c>, so every version NVIDIA had already fetched for the user was invisible:
/// the app could write a .bin and verify its own, but could not see NVIDIA's.
///
/// This scanner closes that gap in the only direction that costs nothing: it READS. No network
/// call, no download, no payload fetched from an NVIDIA endpoint — the bytes are already on disk
/// because the driver put them there. That is also why this path carries none of the
/// redistribution question that <see cref="OtaPayloadDownloader"/> does.
///
/// SCOPE — dlss, dlssg, dlssd only. Verified against a live bucket listing of NVIDIA's OTA
/// channel: those three are the components whose leaf payload is a single <c>.bin</c> (one renamed
/// DLL). <c>dlss_override</c> and <c>sl_sdk_0</c> ship <c>.zip</c> bundles instead, and
/// <c>dlssnr</c> does not appear in the channel AT ALL — zero keys — which independently confirms
/// the v0.65 decision to keep NR out of <c>channelByDll</c>. NR absence here is normal and must
/// never be reported as an error.
///
/// VERSION AUTHORITY — always the PE bytes, never the folder name. The same component directory
/// mixes generations: real listings contain <c>131356</c> (decodes to 2.1.28, a legacy DLSS 2.x
/// build) as a sibling of <c>20318080</c> (310.7.128). Ordering by decoded folder name therefore
/// ranks across two unrelated version lines. <see cref="DllVersionReader.ReadFileVersion"/> reads
/// a <c>.bin</c> exactly as it reads a <c>.dll</c> — FileVersionInfo does not care about the
/// extension, and an OTA <c>.bin</c> was confirmed byte-for-byte identical (SHA-256) to the
/// real-named DLL NVIDIA ships for the same version. The packed folder name is kept only as a
/// diagnostic and as a last-resort display value.
///
/// READ-ONLY BY CONSTRUCTION. Nothing here writes, and the OTA cache root must never enter
/// <see cref="NgxPathResolver.WriteRoots"/> — installing a harvested component goes through
/// <see cref="LocalDllImportService"/>, the single existing write funnel, which already backs up,
/// writes both payload shapes, and reports <c>ImportedComponent</c>/<c>Landed</c> accounting.
/// </summary>
public class OtaCacheScanner
{
    /// <summary>Source tag for entries harvested from the production OTA cache.</summary>
    public const string SourceProduction = "NGX_OTA";

    /// <summary>Source tag for entries harvested from the staging OTA cache.</summary>
    public const string SourceStaging = "NGX_OTA_Staging";

    /// <summary>
    /// The OTA-delivered components this scanner harvests, as DLL name → component directory.
    ///
    /// Deliberately a SUBSET of <see cref="NgxModelLayout.ComponentDirByDll"/> and derived from it,
    /// so the canonical DLL set stays defined once. The subset is not an arbitrary preference: it
    /// is the set whose OTA leaf is a single renamed DLL. Asserted against the parent map by test,
    /// so adding a sixth component to the canonical set cannot silently bypass this decision.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> HarvestableComponents =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nvngx_dlss.dll"]  = "dlss",
            ["nvngx_dlssg.dll"] = "dlssg",
            ["nvngx_dlssd.dll"] = "dlssd",
        };

    /// <summary>
    /// Payload leaf filename: <c>&lt;archPrefix&gt;_&lt;appId&gt;.bin</c>, e.g. <c>160_E658700.bin</c>.
    ///
    /// Both fields are matched as hex rather than pinned to known values on purpose. The live
    /// channel carries arch prefixes 160/170/180/190/1B0 and, besides the two generic app ids
    /// (E658700, E658703), a long tail of per-title CMS ids (B9CF688, B9D48D0, E99B5EC, ...).
    /// Hardcoding the generics would silently skip every per-title payload on the machine.
    /// </summary>
    private static readonly Regex PayloadLeafRegex =
        new(@"^(?<arch>[0-9A-Fa-f]{2,3})_(?<appId>[0-9A-Fa-f]+)\.bin$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>True for a <c>.sha256</c> sidecar, which is metadata and never a payload.</summary>
    private static bool IsSidecar(string fileName) =>
        fileName.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One harvested payload: which component, what version its bytes report, and where it lives.
    /// </summary>
    public sealed record HarvestedPayload(
        string ComponentDir,
        string DllName,
        string Version,
        string PackedFolder,
        string PayloadPath,
        string Source)
    {
        /// <summary>True when the version came from the PE bytes rather than the folder name.</summary>
        public bool VersionFromBytes => DllVersionReader.IsValidVersion(Version);
    }

    /// <summary>
    /// Scans one NGX base path's OTA cache (production and staging trees) and returns every
    /// harvestable payload found. Never throws; access failures land in <paramref name="errors"/>
    /// when supplied so an empty result is never the only symptom.
    /// </summary>
    public List<HarvestedPayload> Harvest(string ngxBasePath, List<string>? errors = null)
    {
        var results = new List<HarvestedPayload>();
        if (string.IsNullOrWhiteSpace(ngxBasePath))
            return results;

        // Production first, then staging: same skeleton, different root, per NVIDIA's own
        // CDNServerType split (0 = production, 1 = staging).
        HarvestTree(Path.Combine(ngxBasePath, "models"), SourceProduction, results, errors);
        HarvestTree(Path.Combine(ngxBasePath, "Staging", "models"), SourceStaging, results, errors);
        return results;
    }

    private void HarvestTree(
        string modelsRoot, string source, List<HarvestedPayload> results, List<string>? errors)
    {
        if (!Directory.Exists(modelsRoot))
            return;

        foreach (var (dllName, componentDir) in HarvestableComponents)
        {
            var versionsRoot = Path.Combine(modelsRoot, componentDir, "versions");
            if (!Directory.Exists(versionsRoot))
                continue;   // Normal: NVIDIA only caches what this machine actually asked for.

            try
            {
                foreach (var versionFolder in Directory.GetDirectories(versionsRoot))
                {
                    var folderName = Path.GetFileName(versionFolder);

                    // Packed integers only. A non-packed name here is not ours to interpret.
                    if (!NgxModelLayout.IsPackedVersionFolderName(folderName))
                        continue;

                    var filesLeaf = Path.Combine(versionFolder, NgxModelLayout.FilesLeaf);
                    if (!Directory.Exists(filesLeaf))
                        continue;

                    foreach (var payload in Directory.GetFiles(filesLeaf))
                    {
                        var leafName = Path.GetFileName(payload);
                        if (IsSidecar(leafName) || !PayloadLeafRegex.IsMatch(leafName))
                            continue;

                        // THE version authority: the bytes. The folder name is a hint that can
                        // encode a different version line entirely (see class remarks).
                        var fromBytes = DllVersionReader.ReadFileVersion(payload);
                        var version = DllVersionReader.IsValidVersion(fromBytes)
                            ? fromBytes!
                            : "Unknown";   // Present but unreadable — NOT absent. (v0.68 split.)

                        results.Add(new HarvestedPayload(
                            ComponentDir: componentDir,
                            DllName: dllName,
                            Version: version,
                            PackedFolder: folderName,
                            PayloadPath: payload,
                            Source: source));
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                // The one genuinely unsettled question about this tree is its ACLs on a real
                // machine. Report it rather than silently returning nothing.
                Debug.WriteLine($"OtaCacheScanner: access denied to {versionsRoot}: {ex.Message}");
                errors?.Add($"Access denied reading NVIDIA's OTA cache at {versionsRoot} — " +
                            "versions downloaded by the driver are not shown.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OtaCacheScanner: error scanning {versionsRoot}: {ex.Message}");
                errors?.Add($"Error reading OTA cache at {versionsRoot}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Collapses harvested payloads into grid rows, one per (source, version).
    ///
    /// The OTA cache versions each component independently — dlss may be at 310.7.128 while dlssg
    /// sits at 310.6.0 — so a row shows the components that actually exist at that version and
    /// <c>"—"</c> (absent) for the rest. That is the v0.68 status vocabulary: <c>"—"</c> means not
    /// there, <c>"Unknown"</c> means there but unreadable, <c>"N/A"</c> means inapplicable. NR and
    /// DeepDVC are always <c>"N/A"</c> here because NVIDIA's OTA channel does not carry them.
    /// </summary>
    public List<DLSSVersionEntry> ToEntries(IEnumerable<HarvestedPayload> payloads)
    {
        var entries = new List<DLSSVersionEntry>();

        foreach (var group in payloads
                     .GroupBy(p => (p.Source, p.Version))
                     .OrderByDescending(g => ParseForSort(g.Key.Version)))
        {
            string ComponentVersion(string dllName)
            {
                var hit = group.FirstOrDefault(p =>
                    string.Equals(p.DllName, dllName, StringComparison.OrdinalIgnoreCase));
                return hit is null ? "—" : hit.Version;
            }

            // Any payload in the group carries a usable path; prefer dlss for the row's location.
            var anchor = group.FirstOrDefault(p =>
                             string.Equals(p.DllName, "nvngx_dlss.dll", StringComparison.OrdinalIgnoreCase))
                         ?? group.First();

            entries.Add(new DLSSVersionEntry
            {
                Source = group.Key.Source,
                BuildID = group.Key.Version,
                DLSS = ComponentVersion("nvngx_dlss.dll"),
                FrameGen = ComponentVersion("nvngx_dlssg.dll"),
                DLSSD = ComponentVersion("nvngx_dlssd.dll"),
                // Not carried by NVIDIA's OTA channel — inapplicable, not missing.
                DeepDVC = "N/A",
                DLSSNR = "N/A",
                // No sl.common.dll in the nvngx component tree.
                Streamline = "N/A",
                Path = Directory.GetParent(anchor.PayloadPath)?.FullName ?? anchor.PayloadPath,
                ScannedAt = DateTime.UtcNow,
            });
        }

        return entries;
    }

    /// <summary>
    /// Sort key. Pads to 4 parts so "310.7" and "310.7.0.0" compare equal, and sorts unreadable
    /// entries last instead of throwing.
    /// </summary>
    private static Version ParseForSort(string? version) =>
        DllVersionReader.IsValidVersion(version) && Version.TryParse(Pad(version!), out var v)
            ? v
            : new Version(0, 0, 0, 0);

    private static string Pad(string version)
    {
        var parts = version.Split('.').Length;
        return parts >= 4 ? version : version + string.Concat(Enumerable.Repeat(".0", 4 - parts));
    }
}
