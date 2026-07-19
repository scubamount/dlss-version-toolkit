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

    // --- Persisted preset selections (v0.0.38) ---
    // Stored as strings/ints so a hand-edited or partially-corrupt settings.json degrades to
    // the defaults instead of failing deserialization. Empty string = "not saved yet" — the
    // ViewModel falls back to the recommended defaults (SR=L, RR=E, FG=B, mode=Dynamic, 6x).
    // Before v0.0.38 NONE of these were saved, so the app reset to Preset L on every launch.

    /// <summary>Last applied/selected DLSS-SR preset ("K", "L", "Latest", …). Empty = default.</summary>
    public string SelectedSrPreset { get; set; } = "";

    /// <summary>Last applied/selected DLSS-RR preset. Empty = default (E).</summary>
    public string SelectedRrPreset { get; set; } = "";

    /// <summary>Last applied/selected DLSS-FG preset. Empty = default (B).</summary>
    public string SelectedFgPreset { get; set; } = "";

    /// <summary>Last selected DLSSG generator mode ("Dynamic", "On", …). Empty = default (Dynamic).</summary>
    public string SelectedFgMode { get; set; } = "";

    /// <summary>Last selected FG multiplier (2..6). 0 = not saved (use default).</summary>
    public int SelectedFgMultiplier { get; set; } = 0;
}