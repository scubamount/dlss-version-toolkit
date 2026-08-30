namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;
using System.Diagnostics;

public interface INgxScanner
{
    List<DLSSVersionEntry> Scan(string ngxBasePath);

    /// <summary>
    /// Same scan, with per-path failures collected into <paramref name="errors"/> instead of
    /// vanishing into Debug.WriteLine. Existing callers keep the 1-arg overload (v0.0.57).
    /// </summary>
    List<DLSSVersionEntry> Scan(string ngxBasePath, List<string> errors);
}

public class NgxScanner : INgxScanner
{
    private readonly INgxConfigParser _configParser;

    public const string ReleaseSubPath = @"models\dlss_override\versions";

    /// <summary>
    /// Prefix of the backup folders <see cref="BackupService"/> creates as siblings of the
    /// version folders, and the suffix of the transient "aside" folder a restore renames the
    /// current release to. Both live INSIDE the versions parent, so every enumerator of that
    /// directory must skip them or it reports our own bookkeeping as installed NGX versions.
    /// </summary>
    public const string BackupFolderPrefix = ".dlss-backup-";
    public const string RestoreAsideSuffix = ".restoring";

    /// <summary>
    /// Canonical test for "this directory under versions\ is a real NGX version folder".
    /// NVIDIA uses TWO naming schemes here and both are real:
    ///   * dotted  — "310.7.0.0", what the dlss_override tree carries;
    ///   * packed  — "20318080", a major&lt;&lt;16|minor&lt;&lt;8|patch integer, what the driver's own
    ///     model tree uses (see <see cref="NgxModelLayout"/>).
    /// Backup folders (.dlss-backup-*) and the transient restore-aside folder (*.restoring) are
    /// ours and must never be scanned, listed as an installed version, or chosen as a restore
    /// target.
    ///
    /// Single predicate on purpose: NgxScanner (display), BackupsDialog (restore target) and
    /// any future enumerator answer this question the same way. Two copies of this rule drift
    /// — that is exactly how the v0.0.43 lexical-sort bug survived in a sibling.
    /// </summary>
    public static bool IsVersionFolderName(string folderName) =>
        !string.IsNullOrEmpty(folderName) &&
        !folderName.StartsWith(BackupFolderPrefix, StringComparison.OrdinalIgnoreCase) &&
        !folderName.EndsWith(RestoreAsideSuffix, StringComparison.OrdinalIgnoreCase) &&
        NgxModelLayout.ParseVersionFolderName(folderName) != null;

    /// <summary>
    /// Orders version folder paths newest-first NUMERICALLY, across BOTH naming schemes.
    ///
    /// Ordinal string ordering puts 310.9.0.0 above 310.10.0.0 — the defect class fixed in
    /// GetCachedVersions in v0.0.43. Naive Version.TryParse has a second, worse failure here: a
    /// packed name like "20318080" padded to "20318080.0" parses as Version(20318080, 0) and
    /// dwarfs (310,7,0,0), so a packed folder always sorted newest regardless of what it encodes.
    /// Decoding through <see cref="NgxModelLayout.ParseVersionFolderName"/> makes the two schemes
    /// genuinely comparable: 20318080 decodes to 310.7.128, which really is newer than 310.7.0 —
    /// now for the right reason. Unparseable names sort last rather than throwing.
    /// </summary>
    public static IEnumerable<string> OrderVersionFoldersNewestFirst(IEnumerable<string> folderPaths) =>
        folderPaths.OrderByDescending(p =>
            NgxModelLayout.ParseVersionFolderName(Path.GetFileName(p)) ?? new Version(0, 0));
    private static readonly string StagingSubPath = @"Staging\models\dlss_override\versions";

    public NgxScanner(INgxConfigParser configParser)
    {
        _configParser = configParser;
    }

    /// <summary>Scans one NGX base path; failures are swallowed into Debug only.</summary>
    public List<DLSSVersionEntry> Scan(string ngxBasePath) => Scan(ngxBasePath, errors: null);

    /// <summary>
    /// Real implementation. When <paramref name="errors"/> is non-null, access-denied and
    /// unexpected scan exceptions are appended as human-readable strings so ScanService can
    /// land them in ScanResult.Errors — an empty grid must never be the only symptom.
    /// </summary>
    public List<DLSSVersionEntry> Scan(string ngxBasePath, List<string>? errors)
    {
        var results = new List<DLSSVersionEntry>();

        // Scan Release
        string releasePath = Path.Combine(ngxBasePath, ReleaseSubPath);
        if (Directory.Exists(releasePath))
        {
            results.AddRange(ScanFolder(releasePath, "NGX_Release", errors));
        }

        // Scan Staging
        string stagingPath = Path.Combine(ngxBasePath, StagingSubPath);
        if (Directory.Exists(stagingPath))
        {
            results.AddRange(ScanFolder(stagingPath, "NGX_Staging", errors));
        }

        return results;
    }

    private List<DLSSVersionEntry> ScanFolder(string basePath, string source, List<string>? errors = null)
    {
        var results = new List<DLSSVersionEntry>();

        try
        {
            foreach (var versionFolder in Directory.GetDirectories(basePath))
            {
                // Skip our own bookkeeping folders. Backups (.dlss-backup-*) and the transient
                // restore-aside folder are siblings of the real version folders, and every one
                // of them contains a full copy of the NGX DLLs — so without this filter the
                // INSTALLED VERSIONS grid grows a phantom row per backup, each showing a stale
                // version, and the "is an update available" comparison reads those stale rows.
                if (!NgxScanner.IsVersionFolderName(Path.GetFileName(versionFolder)))
                    continue;

                var result = _configParser.Parse(versionFolder);
                if (result.IsReparsePoint) continue;

                var entry = new DLSSVersionEntry
                {
                    Source = source,
                    // Packed folder names ("20318080") are decoded for display ("310.7.128") so the
                    // BuildID column never shows a raw integer. The real path is kept in Path, so
                    // nothing downstream loses the on-disk name.
                    BuildID = NgxModelLayout.DisplayVersionFolderName(Path.GetFileName(versionFolder)),
                    DLSS = result.DLSS,
                    FrameGen = result.FrameGen,
                    DLSSD = result.DLSSD,
                    DeepDVC = result.DeepDVC,
                    DLSSNR = result.DLSSNR,
                    // Streamline version lives only in the SDK folder / cached zip (v0.0.38) —
                    // NGX version folders contain no sl.common.dll, so this column is N/A here.
                    Streamline = "N/A",
                    Path = versionFolder,
                    ScannedAt = DateTime.UtcNow
                };

                results.Add(entry);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"NgxScanner.ScanFolder: access denied to {basePath}: {ex.Message}");
            errors?.Add($"Access denied scanning {basePath} — versions there are not shown.");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NgxScanner.ScanFolder: error scanning {basePath}: {ex.Message}");
            errors?.Add($"Error scanning {basePath}: {ex.Message}");
        }

        return results;
    }
}