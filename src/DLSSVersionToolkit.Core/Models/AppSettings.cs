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

    /// <summary>
    /// Identifies the version-gated preset recommendation the user has already answered, in the
    /// form "dll:minVersion:preset" (v0.0.52). Non-empty means "do not ask again for this
    /// rule" — set whether they accepted or declined, so the prompt is one-time rather than
    /// nagging. Cleared by Reset Selections.
    /// </summary>
    public string DismissedPresetRule { get; set; } = "";

    /// <summary>
    /// Folder the user drops importable DLLs into. Empty = the app default
    /// (%AppData%\DLSSVersionToolkit\Overrides). Mirrors the manifest's own copy so the setting is
    /// visible and editable alongside the other paths.
    /// </summary>
    public string OverrideLibraryPath { get; set; } = "";

    /// <summary>
    /// Show NVIDIA's staging-channel (pre-release) builds in LATEST AVAILABLE (v0.72).
    ///
    /// Off by default: staging runs ahead of production (310.9.0 vs 310.7.128 when this shipped),
    /// so leaving it on for everyone would mark up-to-date machines as behind against a build the
    /// driver will not serve them on its own. When on, a staging version is shown only if it is
    /// strictly newer than every other feed, and it is always labelled as pre-release.
    /// </summary>
    public bool IncludePreReleaseChannel { get; set; } = false;

    /// <summary>
    /// Allow downloading component payloads from NVIDIA's OTA CDN (v0.72).
    ///
    /// Separate from <see cref="IncludePreReleaseChannel"/> on purpose: seeing that a newer build
    /// exists and pulling binaries from an undocumented endpoint are different decisions, and a
    /// user may reasonably want the first without the second. Off by default; the GitHub SDK
    /// remains the default source. Every OTA payload is checked against its published SHA-256 and
    /// its Authenticode signer before it is written into the NGX tree.
    /// </summary>
    public bool AllowOtaPayloadDownloads { get; set; } = false;

}