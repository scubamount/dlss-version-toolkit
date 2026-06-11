namespace DLSSVersionToolkit.Core.Models;

public class AppSettings
{
    public string NgxBasePath { get; set; } = "";
    public string AnWavePath { get; set; } = "";
    public string StreamlinePath { get; set; } = "";
    public bool AutoScanEnabled { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool MinimizeToTray { get; set; } = false;
    public int ScanIntervalHours { get; set; } = 4;
    public bool NotifyOnNewVersion { get; set; } = true;

    /// <summary>Check GitHub for a newer app version on startup (non-blocking, best-effort).</summary>
    public bool CheckForAppUpdates { get; set; } = true;

    /// <summary>Set once the user dismisses the first-run quick guide card.</summary>
    public bool HasSeenQuickGuide { get; set; } = false;
}