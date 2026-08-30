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
    /// <summary>
    /// AnWave/global DLL → component map. Same canonical-derivation rule as StreamlineScanner:
    /// the NGX entries must reconcile with <see cref="UpgradeService.NgxDllNames"/> so a new sync
    /// DLL can never be invisible to the AnWave scan.
    /// </summary>
    private static readonly Dictionary<string, string> DllToComponent = StreamlineScanner.GetDllToComponent();

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
            DLSSNR = "Unknown",
            // Streamline is NOT APPLICABLE to an AnWave/NGX folder (no sl.common.dll lives there),
            // which is a different fact from "we could not determine it". NgxScanner already
            // reports "N/A" for the same column; saying "Unknown" here made two rows in the same
            // grid describe one fact with two words.
            Streamline = "N/A",
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
                    case "dlssnr":
                        entry.DLSSNR = version;
                        break;
                    case "streamline":
                        entry.Streamline = version;
                        break;
                }
            }
        }

        return foundAny ? entry : null;
    }

    /// <summary>
    /// Reads a DLL's version through <see cref="DllVersionReader"/> (the single source of truth
    /// for "what version is this DLL", including comma-form normalization) and rejects anything
    /// that isn't a plausible version string. Returns "Unknown" when unreadable.
    /// </summary>
    private static string GetDllVersion(string dllPath)
    {
        var version = DllVersionReader.ReadFileVersion(dllPath);
        // One validity rule for the whole tree (v0.0.61): DllVersionReader.IsValidVersion.
        return DllVersionReader.IsValidVersion(version) ? version! : "Unknown";
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