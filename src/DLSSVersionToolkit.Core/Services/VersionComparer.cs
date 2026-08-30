namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;

public interface IVersionComparer
{
    void MarkNewest(ScanResult result);
    List<Recommendation> GenerateRecommendations(ScanResult result);

    /// <summary>
    /// True when <paramref name="candidate"/> is a strictly newer DLSS version than
    /// <paramref name="baseline"/>, using numeric 4-part comparison (NOT lexical string
    /// compare, which mis-orders e.g. 310.6 vs 310.10). "Unknown"/"N/A" handled safely.
    /// </summary>
    bool IsNewer(string candidate, string baseline);
}

public class VersionComparer : IVersionComparer
{
    public void MarkNewest(ScanResult result)
    {
        // Reset all flags
        foreach (var entry in result.Sources)
        {
            entry.IsNewestDLSS = false;
            entry.IsNewestFG = false;
            entry.IsNewestDLSSD = false;
            entry.IsNewestDeepDVC = false;
            entry.IsNewestDLSSNR = false;
        }

        var components = new[] { "DLSS", "FrameGen", "DLSSD", "DeepDVC", "DLSSNR" };

        foreach (var component in components)
        {
            VersionInfo? newest = null;
            DLSSVersionEntry? newestEntry = null;

            foreach (var entry in result.Sources)
            {
                var version = component switch
                {
                    "DLSS" => entry.DLSS,
                    "FrameGen" => entry.FrameGen,
                    "DLSSD" => entry.DLSSD,
                    "DeepDVC" => entry.DeepDVC,
                    "DLSSNR" => entry.DLSSNR,
                    _ => "Unknown"
                };

                if (version == "Unknown" || version == "N/A")
                    continue;

                if (newest == null || IsVersionNewer(version, newest.Version))
                {
                    newest = new VersionInfo(version, entry.Source);
                    newestEntry = entry;
                }
            }

            if (newestEntry != null && newest != null)
            {
                result.NewestPerComponent[component] = newest;
                switch (component)
                {
                    case "DLSS": newestEntry.IsNewestDLSS = true; break;
                    case "FrameGen": newestEntry.IsNewestFG = true; break;
                    case "DLSSD": newestEntry.IsNewestDLSSD = true; break;
                    case "DeepDVC": newestEntry.IsNewestDeepDVC = true; break;
                    case "DLSSNR": newestEntry.IsNewestDLSSNR = true; break;
                }
            }
        }
    }

    public List<Recommendation> GenerateRecommendations(ScanResult result)
    {
        var recommendations = new List<Recommendation>();

        var ngxRelease = result.Sources.FirstOrDefault(s => s.Source == "NGX_Release");
        if (ngxRelease == null)
            return recommendations;

        // Find newest versions
        var newestDLSS = result.NewestPerComponent.GetValueOrDefault("DLSS");
        var newestFG = result.NewestPerComponent.GetValueOrDefault("FrameGen");

        if (newestDLSS != null && newestDLSS.Source != "NGX_Release")
        {
            var sourceEntry = result.Sources.FirstOrDefault(s => s.Source == newestDLSS.Source);
            if (sourceEntry != null && IsVersionNewer(sourceEntry.DLSS, ngxRelease.DLSS))
            {
                recommendations.Add(new Recommendation
                {
                    Action = "Update_NGX_from_" + newestDLSS.Source,
                    Description = $"{newestDLSS.Source} has newer DLSS ({sourceEntry.DLSS}) than NGX Release ({ngxRelease.DLSS})",
                    FromSource = newestDLSS.Source,
                    ToTarget = "NGX_Release"
                });
            }
        }

        if (recommendations.Count == 0 && result.Sources.Count > 0)
        {
            recommendations.Add(new Recommendation
            {
                Action = "UpToDate",
                Description = "All sources already at newest version",
                FromSource = "",
                ToTarget = ""
            });
        }

        return recommendations;
    }

    /// <summary>Public numeric version comparison — delegates to the same 4-part logic
    /// MarkNewest uses, so UI availability checks and table highlighting never disagree.</summary>
    public bool IsNewer(string candidate, string baseline) => IsVersionNewer(candidate, baseline);

    private static bool IsVersionNewer(string version1, string version2)
    {
        if (version1 == "Unknown" || version1 == "N/A") return false;
        if (version2 == "Unknown" || version2 == "N/A") return true;
        try
        {
            // Normalize: remove letters, trim to 4 parts
            var v1 = NormalizeVersion(version1);
            var v2 = NormalizeVersion(version2);

            // Pad to exactly 4 components so 2-part versions (e.g. "310.6") don't
            // IndexOutOfRange at parts[i] — caught as "never newer" before this fix.
            var parts1 = v1.Split('.').Take(4)
                .Select(p => int.TryParse(p, out var n) ? n : 0)
                .Concat(Enumerable.Repeat(0, 4)).Take(4).ToArray();
            var parts2 = v2.Split('.').Take(4)
                .Select(p => int.TryParse(p, out var n) ? n : 0)
                .Concat(Enumerable.Repeat(0, 4)).Take(4).ToArray();

            for (int i = 0; i < 4; i++)
            {
                if (parts1[i] > parts2[i]) return true;
                if (parts1[i] < parts2[i]) return false;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizeVersion(string version)
    {
        // Remove letters, take first 4 parts
        var cleaned = System.Text.RegularExpressions.Regex.Replace(version, @"[a-zA-Z]", "");
        var parts = cleaned.Split('.').Take(4);
        return string.Join(".", parts);
    }
}