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