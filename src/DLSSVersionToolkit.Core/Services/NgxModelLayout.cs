namespace DLSSVersionToolkit.Core.Services;

using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Single source of truth for NVIDIA's NGX *model* tree layout — the per-component
/// <c>models\{component}\versions\{packedVersion}\files\{archPrefix}_{appId}.bin</c> form that the
/// driver's NGX loader actually reads, as distinct from the flat
/// <c>models\dlss_override\versions\{dotted}\nvngx_*.dll</c> tree the rest of this toolkit writes.
///
/// WHY THIS EXISTS (v0.0.51). Two independently-sourced observations:
///
///   1. A real nvidiaDlssGlom ("NVIDIA DLSS Override Update Tool" v2.4.10.2) run log writes to
///      <c>...\Staging\models\dlss\versions\20318080\files\160_E658703.bin</c> — the DLL is RENAMED
///      to an arch+appId <c>.bin</c>, and the version folder is an 8-digit integer.
///   2. emoose/DLSSTweaks issue #137 documents the same scheme two years earlier: folder
///      <c>198400</c> + <c>files\160_E658703.bin</c> made the driver globally load DLSS 3.7.0.
///
/// The folder name is a PACKED integer: <c>major &lt;&lt; 16 | minor &lt;&lt; 8 | patch</c>.
/// Verified round-trip against four independent data points:
///   20318080 = 310.7.128  (log's dlss/dlssg/dlssd folder; UI showed nvngx_dlss.dll 310.7.128.0)
///   20317952 = 310.7.0    (log's deepdvc folder;          UI showed nvngx_deepdvc.dll 310.7.0.0)
///     134144 =   2.12.0   (log's sl_* folders;            UI showed sl.common.dll 2.12.0.0)
///     198400 =   3.7.0    (emoose #137, stated as 3.7.0)
///
/// This matters beyond the import feature: those packed folders are numeric, so the pre-v0.0.51
/// <see cref="NgxScanner.IsVersionFolderName"/> accepted them and
/// <see cref="NgxScanner.OrderVersionFoldersNewestFirst"/> keyed "20318080" as Version(20318080,0)
/// — dwarfing (310,7,0,0) and displaying a raw integer as the BuildID.
/// </summary>
public static class NgxModelLayout
{
    /// <summary>
    /// Per-component model directory names, keyed by NGX DLL name. These are the
    /// <c>models\&lt;name&gt;\</c> directories in the glom log, and they are NOT the same thing as
    /// the single flat <c>dlss_override</c> directory.
    /// Derived from <see cref="UpgradeService.NgxDllNames"/> ordering on purpose: the canonical DLL
    /// set is defined once, and this map must cover exactly it (asserted by tests).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ComponentDirByDll =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nvngx_dlss.dll"]     = "dlss",
            ["nvngx_dlssg.dll"]    = "dlssg",
            ["nvngx_dlssd.dll"]    = "dlssd",
            ["nvngx_deepdvc.dll"]  = "deepdvc",
        };

    /// <summary>
    /// The generic NGX application id the driver falls back to when a game does not supply its own.
    /// Observed in both the glom log and emoose #137 as the <c>_E658703</c> filename suffix; the log
    /// also writes an <c>_E658700</c> twin for every component, so both are produced.
    /// </summary>
    public static readonly string[] GenericAppIds = { "E658703", "E658700" };

    /// <summary>
    /// GPU-architecture filename prefixes seen in the glom log's sl_* writes, hex-ish tags the NGX
    /// loader matches against the running GPU. <c>160</c> (Turing) is what both the log and
    /// emoose #137 use for the nvngx component <c>.bin</c> files.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> ArchPrefixes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Volta"]      = "140",
            ["Turing"]     = "160",
            ["Ampere"]     = "170",
            ["Hopper"]     = "180",
            ["Ada"]        = "190",
            ["Blackwell"]  = "1A0",
            ["Blackwell2"] = "1B0",
        };

    /// <summary>Default arch prefix for nvngx component .bin files (Turing), per both sources.</summary>
    public const string DefaultArchPrefix = "160";

    /// <summary>The <c>files</c> leaf directory that holds the renamed .bin payloads.</summary>
    public const string FilesLeaf = "files";

    /// <summary>A packed version folder name is a bare integer with no dots.</summary>
    private static readonly Regex PackedFolderRegex = new(@"^\d+$", RegexOptions.Compiled);

    /// <summary>
    /// True when a version-folder name is NVIDIA's packed-integer form ("20318080") rather than the
    /// dotted form ("310.7.0.0"). Bare integers below <see cref="MinPackedValue"/> are rejected so a
    /// folder literally named "0" or "7" is not mistaken for a packed version.
    /// </summary>
    public static bool IsPackedVersionFolderName(string? folderName) =>
        !string.IsNullOrEmpty(folderName) &&
        PackedFolderRegex.IsMatch(folderName) &&
        long.TryParse(folderName, out var v) &&
        v >= MinPackedValue &&
        v <= uint.MaxValue;

    /// <summary>
    /// Smallest value treated as packed. 65536 == 1.0.0, so anything below cannot encode a real
    /// major version and is far more likely to be a stray folder.
    /// </summary>
    public const long MinPackedValue = 65536;

    /// <summary>
    /// Decodes a packed NGX version integer into a <see cref="Version"/>:
    /// <c>major = n &gt;&gt; 16</c>, <c>minor = (n &gt;&gt; 8) &amp; 0xFF</c>, <c>patch = n &amp; 0xFF</c>.
    /// Returns null when the name is not a packed form.
    /// </summary>
    public static Version? DecodePackedVersion(string? folderName)
    {
        if (!IsPackedVersionFolderName(folderName))
            return null;
        if (!long.TryParse(folderName, out var n))
            return null;
        return DecodePackedVersion((uint)n);
    }

    /// <summary>Decodes a packed NGX version integer into a <see cref="Version"/>.</summary>
    public static Version DecodePackedVersion(uint packed) =>
        new((int)(packed >> 16), (int)((packed >> 8) & 0xFF), (int)(packed & 0xFF));

    /// <summary>
    /// Encodes major/minor/patch into NVIDIA's packed folder integer. Minor and patch must fit in a
    /// byte — the encoding has no room for more, so an out-of-range component is a hard error rather
    /// than a silently truncated (and wrong) folder name.
    /// </summary>
    public static uint EncodePackedVersion(int major, int minor, int patch)
    {
        if (major < 0 || major > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(major), major, "major must fit in 16 bits.");
        if (minor is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(minor), minor, "minor must fit in 8 bits.");
        if (patch is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(patch), patch, "patch must fit in 8 bits.");
        return (uint)((major << 16) | (minor << 8) | patch);
    }

    /// <summary>
    /// Encodes a dotted version string ("310.7.128" or "310.7.128.0") to its packed folder name.
    /// The 4th component is ignored: the packed form has no field for it, and every observed NGX
    /// folder encodes only major.minor.patch. Returns null when the input cannot be parsed or a
    /// component does not fit the encoding.
    /// </summary>
    public static string? EncodePackedFolderName(string? dottedVersion)
    {
        if (string.IsNullOrWhiteSpace(dottedVersion))
            return null;

        var parts = dottedVersion.Trim().Replace(',', '.').Split('.');
        if (parts.Length < 2)
            return null;

        if (!int.TryParse(parts[0], out var major)) return null;
        if (!int.TryParse(parts[1], out var minor)) return null;
        var patch = 0;
        if (parts.Length >= 3 && !int.TryParse(parts[2], out patch)) return null;

        try
        {
            return EncodePackedVersion(major, minor, patch).ToString();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Normalizes ANY NGX version folder name — packed or dotted — to a comparable
    /// <see cref="Version"/>. This is the one function that makes the two naming schemes sortable
    /// against each other; before it existed a packed name compared as its raw integer and always
    /// won. Returns null when the name is neither form.
    /// </summary>
    public static Version? ParseVersionFolderName(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName))
            return null;

        var packed = DecodePackedVersion(folderName);
        if (packed != null)
            return packed;

        // Dotted form. Version.TryParse needs 2-4 components.
        var parts = folderName.Split('.');
        var candidate = parts.Length == 1
            ? folderName + ".0"
            : parts.Length > 4 ? string.Join('.', parts.Take(4)) : folderName;

        return Version.TryParse(candidate, out var v) ? v : null;
    }

    /// <summary>
    /// Renders a version folder name for DISPLAY. Packed names are decoded ("20318080" →
    /// "310.7.128") so the UI never shows a raw 8-digit integer as a build id; dotted names pass
    /// through unchanged.
    /// </summary>
    public static string DisplayVersionFolderName(string? folderName)
    {
        if (string.IsNullOrEmpty(folderName))
            return string.Empty;
        var packed = DecodePackedVersion(folderName);
        return packed != null ? packed.ToString(3) : folderName;
    }

    /// <summary>
    /// Builds the model-tree directory that holds a component's .bin payloads:
    /// <c>{ngxBase}\[Staging\]models\{component}\versions\{packed}\files</c>.
    /// </summary>
    /// <param name="ngxBasePath">NGX base, e.g. %ProgramData%\NVIDIA\NGX.</param>
    /// <param name="componentDir">Component directory name, e.g. "dlss" (see <see cref="ComponentDirByDll"/>).</param>
    /// <param name="packedVersion">Packed version folder name, e.g. "20318080".</param>
    /// <param name="staging">Target the Staging tree instead of production.</param>
    public static string GetComponentFilesDir(string ngxBasePath, string componentDir, string packedVersion, bool staging)
    {
        var segments = staging
            ? new[] { ngxBasePath, "Staging", "models", componentDir, "versions", packedVersion, FilesLeaf }
            : new[] { ngxBasePath, "models", componentDir, "versions", packedVersion, FilesLeaf };
        return Path.Combine(segments);
    }

    /// <summary>
    /// The .bin file names a single component DLL must be written as — one per generic app id,
    /// e.g. <c>160_E658703.bin</c> and <c>160_E658700.bin</c>.
    /// </summary>
    public static IEnumerable<string> GetBinFileNames(string archPrefix = DefaultArchPrefix) =>
        GenericAppIds.Select(appId => $"{archPrefix}_{appId}.bin");
}
