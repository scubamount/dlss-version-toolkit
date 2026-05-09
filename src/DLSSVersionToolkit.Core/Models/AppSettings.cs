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
}