namespace DLSSVersionToolkit.Core.Services;

using System.IO;
using System.Text.RegularExpressions;

public interface INgxConfigParser
{
    NgxConfigResult Parse(string folderPath);
}

public class NgxConfigResult
{
    public string DLSS { get; set; } = "Unknown";
    public string FrameGen { get; set; } = "Unknown";
    public string DLSSD { get; set; } = "Unknown";
    public string DeepDVC { get; set; } = "Unknown";
    public string DLSSNR { get; set; } = "Unknown";
    public string? Message { get; set; }
    public bool IsReparsePoint { get; set; }
    public bool IsCorrupt { get; set; }
}

public class NgxConfigParser : INgxConfigParser
{
    private const string ConfigFileName = "nvngx_package_config.txt";

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
            // No sidecar config (current SDK zips ship none). Fall back to the authoritative
            // DLL FileVersionInfo so a folder containing only DLLs still reports a real version.
            OverrideVersionsFromDlls(folderPath, result);
            result.Message = result.DLSS != "Unknown" ? "Version from DLL (no config)" : "Config file not found";
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

            // Parse each component from the config (legacy/sidecar source).
            result.DLSS = ParseComponent(content, "dlss");
            result.FrameGen = ParseComponent(content, "dlssg");
            result.DLSSD = ParseComponent(content, "dlssd");
            result.DeepDVC = ParseComponent(content, "deepdvc");
            result.DLSSNR = ParseComponent(content, "dlssnr");
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

        // AUTHORITATIVE OVERRIDE: the actual DLL bytes are the source of truth for the installed
        // version. The nvngx_package_config.txt goes stale when newer DLLs are copied into an
        // existing version folder (the SDK zips ship no config to overwrite it with), which made
        // the scanner keep reporting the OLD version after a successful sync — and "update
        // available" never cleared. Read each component's version from its DLL's FileVersionInfo
        // when present; fall back to the parsed config value otherwise.
        OverrideVersionsFromDlls(folderPath, result);

        return result;
    }

    // Replaces parsed component versions with the real DLL FileVersionInfo when the DLL exists.
    private static void OverrideVersionsFromDlls(string folderPath, NgxConfigResult result)
    {
        try
        {
            var dlss = DllVersionReader.ReadComponentVersion(folderPath, "nvngx_dlss.dll");
            if (!string.IsNullOrEmpty(dlss)) result.DLSS = dlss;

            var dlssg = DllVersionReader.ReadComponentVersion(folderPath, "nvngx_dlssg.dll");
            if (!string.IsNullOrEmpty(dlssg)) result.FrameGen = dlssg;

            var dlssd = DllVersionReader.ReadComponentVersion(folderPath, "nvngx_dlssd.dll");
            if (!string.IsNullOrEmpty(dlssd)) result.DLSSD = dlssd;

            var deepdvc = DllVersionReader.ReadComponentVersion(folderPath, "nvngx_deepdvc.dll");
            if (!string.IsNullOrEmpty(deepdvc)) result.DeepDVC = deepdvc;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"NgxConfigParser.OverrideVersionsFromDlls failed for {folderPath}: {ex.Message}");
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