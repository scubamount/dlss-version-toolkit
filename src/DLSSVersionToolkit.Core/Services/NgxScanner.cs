namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;

public interface INgxScanner
{
    List<DLSSVersionEntry> Scan(string ngxBasePath);
}

public class NgxScanner : INgxScanner
{
    private readonly INgxConfigParser _configParser;
    private static readonly string ReleaseSubPath = @"models\dlss_override\versions";
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
                    Streamline = source == "NGX_Release" ? "N/A" : "N/A",
                    Path = versionFolder,
                    ScannedAt = DateTime.UtcNow
                };

                results.Add(entry);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Access denied — skip
        }
        catch (Exception)
        {
            // Other errors — skip
        }

        return results;
    }
}