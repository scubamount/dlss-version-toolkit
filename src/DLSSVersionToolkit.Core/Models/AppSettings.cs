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
    /// Show NVIDIA's staging-channel (pre-release) builds in LATEST AVAILABLE (v0.72;
    /// default flipped to on in v0.73).
    ///
    /// On by default: this is a version-inspection tool, and hiding a real newer build behind a
    /// setting most users never open makes the tool answer "what exists?" incompletely. Staging
    /// runs ahead of production (310.9.0 vs 310.7.128 when this shipped), so a staging version is
    /// shown only when it is strictly newer than every other feed and is always labelled
    /// "OTA pre-release" — the number is never presented as though production served it.
    /// </summary>
    public bool IncludePreReleaseChannel { get; set; } = true;

    /// <summary>
    /// Allow downloading component payloads from NVIDIA's OTA CDN (v0.72; default flipped to on
    /// in v0.73).
    ///
    /// Separate from <see cref="IncludePreReleaseChannel"/> on purpose: seeing that a newer build
    /// exists and pulling binaries are different decisions, and a user may want the first without
    /// the second. Every OTA payload is checked against its published SHA-256 and its Authenticode
    /// signer before it is written into the NGX tree.
    ///
    /// This consent alone is not sufficient to download. <see cref="OtaRedistributionAccepted"/>
    /// must also be set, and it is only set by an explicit user acceptance — see
    /// OtaPayloadDownloader.IsDownloadPermitted, which is the single predicate the download path
    /// consults.
    /// </summary>
    public bool AllowOtaPayloadDownloads { get; set; } = true;

    /// <summary>
    /// Records that the user has accepted responsibility for fetching NVIDIA-copyrighted
    /// components from NVIDIA's OTA endpoint (v0.73).
    ///
    /// Deliberately NOT defaulted to true, and deliberately separate from
    /// <see cref="AllowOtaPayloadDownloads"/>. The first is a preference ("use this source");
    /// this one is an acceptance ("I understand what is being fetched and from where"), and a
    /// preference default must never stand in for a licensing acceptance nobody made. NVIDIA's
    /// OTA endpoint is undocumented and its redistribution terms are unresolved, so the default
    /// answer to "may this machine pull those bytes?" stays no until a human says otherwise.
    ///
    /// If the app can instead trigger NVIDIA's own OTA updater to fetch components, no
    /// redistribution occurs and this gate becomes unnecessary.
    /// </summary>
    public bool OtaRedistributionAccepted { get; set; } = false;

}