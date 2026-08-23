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
    /// <summary>
    /// NGX DLL → component map. The NGX four derive from <see cref="UpgradeService.NgxDllNames"/>
    /// (canonical set — v0.0.43 proved hardcoded siblings silently drift); sl.common.dll is the
    /// Streamline-only extra. Static so the UI/grid and scan agree forever.
    /// </summary>
    private static readonly Dictionary<string, string> DllToComponent = GetDllToComponent();

    /// <summary>Shared by <see cref="StreamlineScanner"/> and <see cref="GlobalScanner"/> so the
    /// AnWave/global scan and the Streamline scan can never disagree about component names.</summary>
    public static Dictionary<string, string> GetDllToComponent()
    {
        var map = new Dictionary<string, string>
        {
            { "nvngx_dlss.dll", "dlss" },
            { "nvngx_dlssg.dll", "dlssg" },
            { "nvngx_dlssd.dll", "dlssd" },
            { "nvngx_deepdvc.dll", "deepdvc" },
            { "sl.common.dll", "streamline" }
        };
        // Reconcile with the canonical set: anything UpgradeService syncs must be mappable.
        foreach (var dll in UpgradeService.NgxDllNames)
            if (!map.ContainsKey(dll))
                map[dll] = dll.Replace("nvngx_", "").Replace(".dll", "");
        return map;
    }

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

    /// <summary>
    /// Reads a DLL's version through <see cref="DllVersionReader"/> (the single source of truth
    /// for "what version is this DLL", including comma-form normalization) and rejects anything
    /// that isn't a plausible version string. Returns "Unknown" when unreadable.
    /// </summary>
    private static string GetDllVersion(string dllPath)
    {
        var version = DllVersionReader.ReadFileVersion(dllPath);
        return !string.IsNullOrEmpty(version) && IsValidVersionString(version) ? version : "Unknown";
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