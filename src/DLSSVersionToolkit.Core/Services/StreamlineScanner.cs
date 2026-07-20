namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;
using DLSSVersionToolkit.Core.Models;

public interface IStreamlineScanner
{
    DLSSVersionEntry? Scan(string? streamlinePath);
    string? AutoDetectInDownloads();
}

public class StreamlineScanner : IStreamlineScanner
{
    private static readonly Dictionary<string, string> DllToComponent = new()
    {
        { "nvngx_dlss.dll", "dlss" },
        { "nvngx_dlssg.dll", "dlssg" },
        { "nvngx_dlssd.dll", "dlssd" },
        { "nvngx_deepdvc.dll", "deepdvc" },
        { "sl.common.dll", "streamline" }
    };

    public DLSSVersionEntry? Scan(string? streamlinePath)
    {
        if (string.IsNullOrEmpty(streamlinePath))
            return null;

        string binPath = Path.Combine(streamlinePath, "bin", "x64");
        if (!Directory.Exists(binPath))
        {
            // Try the root path directly (in case it's the bin\x64 itself)
            binPath = streamlinePath;
            if (!File.Exists(Path.Combine(binPath, "nvngx_dlss.dll")))
                return null;
        }

        var dllPath = Path.Combine(binPath, "nvngx_dlss.dll");
        if (!File.Exists(dllPath))
            return null;

        var entry = new DLSSVersionEntry
        {
            Source = "StreamlineSDK",
            BuildID = "unknown",
            DLSS = "Unknown",
            FrameGen = "Unknown",
            DLSSD = "Unknown",
            DeepDVC = "Unknown",
            Streamline = "Unknown",
            Path = binPath,
            ScannedAt = DateTime.UtcNow
        };

        bool foundAny = false;

        foreach (var kvp in DllToComponent)
        {
            var fullPath = Path.Combine(binPath, kvp.Key);
            if (File.Exists(fullPath) && !IsReparsePoint(fullPath))
            {
                var version = GetDllVersion(fullPath);
                if (version != "Unknown")
                    foundAny = true;

                switch (kvp.Value)
                {
                    case "dlss":
                        entry.DLSS = version;
                        entry.BuildID = version != "Unknown" ? version : entry.BuildID;
                        break;
                    case "dlssg":
                        entry.FrameGen = version;
                        break;
                    case "dlssd":
                        entry.DLSSD = version;
                        break;
                    case "deepdvc":
                        entry.DeepDVC = version;
                        break;
                    case "streamline":
                        entry.Streamline = version;
                        break;
                }
            }
        }

        return foundAny ? entry : null;
    }

    public string? AutoDetectInDownloads()
    {
        var downloads = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var downloadsPath = Path.Combine(downloads, "Downloads");

        if (!Directory.Exists(downloadsPath))
            return null;

        try
        {
            // v0.0.40: order by parsed version DESC — first-match used to return whichever
            // old manual extract happened to enumerate first (e.g. 2.11.1 shadowing 2.12.0).
            var candidates = Directory.GetDirectories(downloadsPath)
                .Where(d => System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(d), "streamline-sdk", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                .OrderByDescending(d =>
                {
                    var m = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(d), @"(\d+(?:\.\d+)+)");
                    return m.Success && Version.TryParse(m.Groups[1].Value, out var v) ? v : new Version(0, 0);
                })
                .ToList();

            foreach (var candidate in candidates)
            {
                var binPath = Path.Combine(candidate, "bin", "x64", "nvngx_dlss.dll");
                if (File.Exists(binPath))
                    return candidate;
            }
        }
        catch { }

        return null;
    }

    private static string GetDllVersion(string dllPath)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(dllPath);
            if (string.IsNullOrEmpty(vi.FileVersion))
                return "Unknown";

            var version = vi.FileVersion.Replace(',', '.');
            return IsValidVersionString(version) ? version : "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }

    private static bool IsValidVersionString(string version)
    {
        return System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+(\.\d+){1,3}$");
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return attr.HasFlag(FileAttributes.ReparsePoint);
        }
        catch { return false; }
    }
}