namespace DLSSVersionToolkit.Core.Services;

using System.IO;
using System.Text.RegularExpressions;

public interface INgxConfigParser
{
    NgxConfigResult Parse(string folderPath);
}

public class NgxConfigResult
{
    // Default to "absent", not "Unknown": a folder that was never scanned has no component
    // present, and "Unknown" is reserved for a file that IS there but unreadable (v0.68).
    public string DLSS { get; set; } = NgxConfigParser.VersionAbsent;
    public string FrameGen { get; set; } = NgxConfigParser.VersionAbsent;
    public string DLSSD { get; set; } = NgxConfigParser.VersionAbsent;
    public string DeepDVC { get; set; } = NgxConfigParser.VersionAbsent;
    public string DLSSNR { get; set; } = NgxConfigParser.VersionAbsent;
    public string? Message { get; set; }
    public bool IsReparsePoint { get; set; }
    public bool IsCorrupt { get; set; }

    /// <summary>
    /// True when nvngx_package_config.txt names at least one component. This is ACTIVATION
    /// state — diagnostics only. It must never be used to derive a version (v0.68).
    /// </summary>
    public bool ConfigNamesComponents { get; set; }
}

public class NgxConfigParser : INgxConfigParser
{
    private const string ConfigFileName = "nvngx_package_config.txt";

    /// <summary>File is present but its version resource could not be read.</summary>
    public const string VersionUnreadable = "Unknown";

    /// <summary>No such component file in this tree — nothing to report.</summary>
    public const string VersionAbsent = "—";

    /// <summary>
    /// The five NGX components, each paired with the field it fills. One entry per component
    /// keeps "which DLL feeds which column" a single table instead of five copy-pasted blocks
    /// where DLSSNR was silently missing (its column rendered a config value or stale text
    /// through v0.67).
    /// </summary>
    private static readonly (string DllName, Action<NgxConfigResult, string> Assign)[] ComponentAssignments =
    {
        ("nvngx_dlss.dll",    (r, v) => r.DLSS = v),
        ("nvngx_dlssg.dll",   (r, v) => r.FrameGen = v),
        ("nvngx_dlssd.dll",   (r, v) => r.DLSSD = v),
        ("nvngx_dlssnr.dll",  (r, v) => r.DLSSNR = v),
        ("nvngx_deepdvc.dll", (r, v) => r.DeepDVC = v),
    };

    public NgxConfigResult Parse(string folderPath)
    {
        var result = new NgxConfigResult();

        if (!Directory.Exists(folderPath))
        {
            result.Message = "Folder not found";
            return result;
        }

        // Check for reparse points (symlinks/junctions)
        if (IsReparsePoint(folderPath))
        {
            result.Message = "Skipped reparse point";
            result.IsReparsePoint = true;
            return result;
        }

        // Find config file recursively
        string? configPath = FindConfigFile(folderPath);
        if (configPath == null)
        {
            // No sidecar config (current SDK zips ship none). The DLL bytes are the version
            // source either way — the config's absence changes nothing about what we report.
            ReadVersionsFromDlls(folderPath, result);
            result.Message = result.DLSS != VersionAbsent && result.DLSS != VersionUnreadable
                ? "Version from DLL (no config)"
                : "Config file not found";
            return result;
        }

        // Check if config file is a reparse point
        if (IsReparsePoint(configPath))
        {
            result.Message = "Skipped reparse point on config";
            result.IsReparsePoint = true;
            return result;
        }

        try
        {
            string content = File.ReadAllText(configPath);

            // Handle binary data (null bytes = corrupt)
            if (content.Contains('\0'))
            {
                result.Message = "Corrupt config (binary data)";
                result.IsCorrupt = true;
                return result;
            }

            // Empty file
            if (string.IsNullOrWhiteSpace(content))
            {
                result.Message = "Config file empty";
                return result;
            }

            // Large file warning (>1MB)
            if (content.Length > 1048576)
            {
                result.Message = "Config file large, parsing may be slow";
            }

            // The config is ACTIVATION STATE, not version evidence (v0.68). It is parsed for
            // presence/diagnostics only; every version reported to the UI comes from the DLL
            // bytes in ReadVersionsFromDlls below. Reading versions here is what put stale
            // sidecar strings in the INSTALLED VERSIONS grid: the driver rewrites this file on
            // its own schedule, so after a sync it still described the previous build.
            result.ConfigNamesComponents =
                ParseComponent(content, "dlss") != VersionUnreadable ||
                ParseComponent(content, "dlssg") != VersionUnreadable ||
                ParseComponent(content, "dlssd") != VersionUnreadable ||
                ParseComponent(content, "deepdvc") != VersionUnreadable ||
                ParseComponent(content, "dlssnr") != VersionUnreadable;
            result.Message = "Success";
        }
        catch (UnauthorizedAccessException)
        {
            result.Message = "Access denied";
        }
        catch (Exception ex)
        {
            result.Message = $"Read error: {ex.Message}";
        }

        // DLL bytes are the only version authority (v0.68). Every component reads its OWN file;
        // no column may inherit another column's value or a folder-derived string.
        ReadVersionsFromDlls(folderPath, result);

        return result;
    }

    /// <summary>
    /// Reads each NGX component's version from its own DLL's FileVersionInfo. This is the sole
    /// version source for the INSTALLED VERSIONS grid.
    ///
    /// Status codes are distinct facts and must not collapse into one another (v0.68):
    ///   * a precise version  — the file is present and its PE version resource parsed;
    ///   * <see cref="VersionUnreadable"/> ("Unknown") — the file IS present but its version could
    ///     not be read (corrupt/stripped resource). Something is there and it is wrong.
    ///   * <see cref="VersionAbsent"/> ("—") — no such file in this tree. Nothing is wrong; there
    ///     is simply nothing to report.
    /// Before v0.68 a missing file and an unreadable one both read "Unknown", and a stale config
    /// value could occupy a cell whose DLL did not exist at all — which is why the grid showed
    /// 310.6.0.0 for components that were not installed.
    /// </summary>
    private static void ReadVersionsFromDlls(string folderPath, NgxConfigResult result)
    {
        foreach (var (dllName, assign) in ComponentAssignments)
        {
            try
            {
                var version = DllVersionReader.ReadComponentVersion(folderPath, dllName);

                if (version == null)
                {
                    // ReadComponentVersion returns null both for "file absent" and "read failed".
                    // Separate them here so the two facts stay distinguishable in the UI.
                    assign(result, ComponentFileExists(folderPath, dllName)
                        ? VersionUnreadable
                        : VersionAbsent);
                    continue;
                }

                assign(result, DllVersionReader.IsValidVersion(version) ? version : VersionUnreadable);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"NgxConfigParser.ReadVersionsFromDlls failed for {folderPath}/{dllName}: {ex.Message}");
                assign(result, VersionUnreadable);
            }
        }
    }

    private static bool ComponentFileExists(string folderPath, string dllName)
    {
        try
        {
            if (File.Exists(Path.Combine(folderPath, dllName)))
                return true;
            return Directory.GetFiles(folderPath, dllName, SearchOption.AllDirectories).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindConfigFile(string folderPath)
    {
        return Directory.EnumerateFiles(folderPath, ConfigFileName, SearchOption.AllDirectories)
            .FirstOrDefault();
    }

    private static string ParseComponent(string content, string componentName)
    {
        var match = Regex.Match(content, $@"{componentName},\s+([\d.]+)", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var version = match.Groups[1].Value;
            if (DllVersionReader.IsValidVersion(version))
            {
                return version;
            }
        }
        return "Unknown";
    }

    // IsValidVersionFormat lived here as a third private copy of the version-validity regex;
    // deleted v0.0.61 — DllVersionReader.IsValidVersion is the ONE definition.

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var dirInfo = new DirectoryInfo(path);
            return dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            try
            {
                var fileInfo = new FileInfo(path);
                return fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint);
            }
            catch
            {
                return false;
            }
        }
    }
}