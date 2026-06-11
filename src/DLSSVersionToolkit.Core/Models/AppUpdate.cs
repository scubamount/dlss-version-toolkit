namespace DLSSVersionToolkit.Core.Models;

/// <summary>Result of checking GitHub for a newer app release.</summary>
public class AppUpdateInfo
{
    /// <summary>Version of the running app (e.g. "0.0.31").</summary>
    public string CurrentVersion { get; set; } = "";

    /// <summary>Newest version published on GitHub (e.g. "0.0.32"). Empty when unknown.</summary>
    public string LatestVersion { get; set; } = "";

    /// <summary>True when LatestVersion is strictly newer AND a downloadable exe asset exists.</summary>
    public bool IsUpdateAvailable { get; set; }

    /// <summary>browser_download_url of the DLSSVersionToolkit.exe release asset.</summary>
    public string DownloadUrl { get; set; } = "";

    /// <summary>Size in bytes of the exe asset, used as a download integrity check.</summary>
    public long AssetSize { get; set; }

    /// <summary>Release notes body from the GitHub release.</summary>
    public string ReleaseNotes { get; set; } = "";
}

/// <summary>Result of downloading and swapping in a new app executable.</summary>
public class AppUpdateResult
{
    public bool Success { get; set; }

    /// <summary>Path of the (replaced) executable to relaunch.</summary>
    public string ExePath { get; set; } = "";

    /// <summary>User-facing error with a "What to do" section when Success is false.</summary>
    public string ErrorMessage { get; set; } = "";

    public static AppUpdateResult Succeeded(string exePath) =>
        new() { Success = true, ExePath = exePath };

    public static AppUpdateResult Failed(string message) =>
        new() { Success = false, ErrorMessage = message };
}
