namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;
using System.Diagnostics;

public interface INgxScanner
{
    List<DLSSVersionEntry> Scan(string ngxBasePath);
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
    /// NVIDIA names these folders after the driver/DLSS build (e.g. 310.7.0.0), so a real
    /// folder is purely numeric dotted. Backup folders (.dlss-backup-*) and the transient
    /// restore-aside folder (*.restoring) are ours and must never be scanned, listed as an
    /// installed version, or chosen as a restore target.
    ///
    /// Single predicate on purpose: NgxScanner (display), BackupsDialog (restore target) and
    /// any future enumerator answer this question the same way. Two copies of this rule drift
    /// — that is exactly how the v0.0.43 lexical-sort bug survived in a sibling.
    /// </summary>
    public static bool IsVersionFolderName(string folderName) =>
        !string.IsNullOrEmpty(folderName) &&
        !folderName.StartsWith(BackupFolderPrefix, StringComparison.OrdinalIgnoreCase) &&
        !folderName.EndsWith(RestoreAsideSuffix, StringComparison.OrdinalIgnoreCase) &&
        System.Text.RegularExpressions.Regex.IsMatch(folderName, @"^\d+(\.\d+)*$");

    /// <summary>
    /// Orders version folder paths newest-first NUMERICALLY. Ordinal string ordering puts
    /// 310.9.0.0 above 310.10.0.0 — the exact defect class fixed in GetCachedVersions in
    /// v0.0.43. Unparseable components sort last rather than throwing.
    /// </summary>
    public static IEnumerable<string> OrderVersionFoldersNewestFirst(IEnumerable<string> folderPaths) =>
        folderPaths.OrderByDescending(p =>
        {
            var name = Path.GetFileName(p);
            return Version.TryParse(NormalizeForVersionParse(name), out var v) ? v : new Version(0, 0);
        });

    /// <summary>Version.TryParse needs 2-4 components; pad a bare "310" so it parses.</summary>
    private static string NormalizeForVersionParse(string name)
    {
        var parts = name.Split('.');
        if (parts.Length == 1) return name + ".0";
        if (parts.Length > 4) return string.Join('.', parts.Take(4));
        return name;
    }
    private static readonly string StagingSubPath = @"Staging\models\dlss_override\versions";

    public NgxScanner(INgxConfigParser configParser)
    {
        _configParser = configParser;
    }

    public List<DLSSVersionEntry> Scan(string ngxBasePath)
    {
        var results = new List<DLSSVersionEntry>();

        // Scan Release
        string releasePath = Path.Combine(ngxBasePath, ReleaseSubPath);
        if (Directory.Exists(releasePath))
        {
            results.AddRange(ScanFolder(releasePath, "NGX_Release"));
        }

        // Scan Staging
        string stagingPath = Path.Combine(ngxBasePath, StagingSubPath);
        if (Directory.Exists(stagingPath))
        {
            results.AddRange(ScanFolder(stagingPath, "NGX_Staging"));
        }

        return results;
    }

    private List<DLSSVersionEntry> ScanFolder(string basePath, string source)
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
                    BuildID = Path.GetFileName(versionFolder),
                    DLSS = result.DLSS,
                    FrameGen = result.FrameGen,
                    DLSSD = result.DLSSD,
                    DeepDVC = result.DeepDVC,
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
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NgxScanner.ScanFolder: error scanning {basePath}: {ex.Message}");
        }

        return results;
    }
}