namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;
using DLSSVersionToolkit.Core.Models;

public interface IGlobalScanner
{
    DLSSVersionEntry? Scan(string globalPath);
}

public class GlobalScanner : IGlobalScanner
{
    private static readonly Dictionary<string, string> DllToComponent = new()
    {
        { "nvngx_dlss.dll", "dlss" },
        { "nvngx_dlssg.dll", "dlssg" },
        { "nvngx_dlssd.dll", "dlssd" },
        { "nvngx_deepdvc.dll", "deepdvc" },
        { "sl.common.dll", "streamline" }
    };

    public DLSSVersionEntry? Scan(string globalPath)
    {
        if (string.IsNullOrEmpty(globalPath) || !Directory.Exists(globalPath))
            return null;

        if (IsReparsePoint(globalPath))
            return null;

        var entry = new DLSSVersionEntry
        {
            Source = "AnWave",
            BuildID = "unknown",
            DLSS = "Unknown",
            FrameGen = "Unknown",
            DLSSD = "Unknown",
            DeepDVC = "Unknown",
            Streamline = "Unknown",
            Path = globalPath,
            ScannedAt = DateTime.UtcNow
        };

        bool foundAny = false;

        foreach (var kvp in DllToComponent)
        {
            var dllPath = Path.Combine(globalPath, kvp.Key);
            if (File.Exists(dllPath) && !IsReparsePoint(dllPath))
            {
                var version = GetDllVersion(dllPath);
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