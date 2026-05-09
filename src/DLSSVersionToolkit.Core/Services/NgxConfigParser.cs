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
            result.Message = "Config file not found";
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

            // Parse each component
            result.DLSS = ParseComponent(content, "dlss");
            result.FrameGen = ParseComponent(content, "dlssg");
            result.DLSSD = ParseComponent(content, "dlssd");
            result.DeepDVC = ParseComponent(content, "deepdvc");
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

        return result;
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
            if (IsValidVersionFormat(version))
            {
                return version;
            }
        }
        return "Unknown";
    }

    private static bool IsValidVersionFormat(string version)
    {
        return Regex.IsMatch(version, @"^\d+\.\d+(\.\d+){1,3}$");
    }

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