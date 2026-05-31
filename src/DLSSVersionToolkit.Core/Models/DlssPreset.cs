namespace DLSSVersionToolkit.Core.Models;

/// <summary>
/// DLSS render preset override values for the NVIDIA DRS (Driver Registry Settings).
/// These control which DLSS preset the driver uses for upscaling.
/// </summary>
public enum DlssPreset
{
    /// <summary>No override — use driver default.</summary>
    Default = 0x00000000,

    /// <summary>Preset J — alternative to K, less ghosting but more flicker.</summary>
    J = 0x0000000A,

    /// <summary>Preset K — default for DLAA, Balanced, Quality modes.</summary>
    K = 0x0000000B,

    /// <summary>Preset L — default for Ultra Performance mode.</summary>
    L = 0x0000000C,

    /// <summary>Preset M — default for Performance mode.</summary>
    M = 0x0000000D,

    /// <summary>Always use the latest available preset.</summary>
    Latest = 0x00FFFFFF,
}

/// <summary>
/// DRS setting IDs for DLSS preset overrides.
/// Values are from NVIDIA's official NvApiDriverSettings.h.
/// </summary>
public static class DlssPresetSettingIds
{
    // --- Override ENABLE flags ---
    // Setting just the render-preset selection is NOT enough: the driver ignores the
    // preset unless the matching *_OVERRIDE flag is turned ON (value 1). In NVIDIA App /
    // NVIDIA Profile Inspector terms, this is the difference between the override mode
    // being "Custom" (on) versus "Use global default" / "Use 3D app setting" /
    // "Recommended" (off). 0 = off (use default), 1 = on (custom override active).

    /// <summary>NGX_DLSS_SR_OVERRIDE_ID — enable the DLSS Super Resolution override.</summary>
    public const uint SR_OVERRIDE_ENABLE = 0x10E41E01;

    /// <summary>NGX_DLSS_RR_OVERRIDE_ID — enable the DLSS Ray Reconstruction override.</summary>
    public const uint RR_OVERRIDE_ENABLE = 0x10E41E02;

    /// <summary>NGX_DLSS_FG_OVERRIDE_ID — enable the DLSS Frame Generation override.</summary>
    public const uint FG_OVERRIDE_ENABLE = 0x10E41E03;

    /// <summary>Value that turns an override on ("Custom").</summary>
    public const uint OVERRIDE_ON = 1;

    /// <summary>Value that turns an override off ("use default").</summary>
    public const uint OVERRIDE_OFF = 0;

    // --- Render preset SELECTION ---

    /// <summary>NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID — DLSS-SR preset selection.</summary>
    public const uint SR_RENDER_PRESET = 0x10E41DF3;

    /// <summary>NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID — DLSS-RR preset selection.</summary>
    public const uint RR_RENDER_PRESET = 0x10E41DF7;
}

/// <summary>
/// Display metadata for DLSS presets.
/// </summary>
public static class DlssPresetDisplay
{
    /// <summary>Gets the human-readable description for a preset.</summary>
    public static string GetDescription(DlssPreset preset) => preset switch
    {
        DlssPreset.Default => "Default (no override)",
        DlssPreset.J => "Preset J — less ghosting, more flicker",
        DlssPreset.K => "Preset K — DLAA/Balanced/Quality default",
        DlssPreset.L => "Preset L — Ultra Performance default",
        DlssPreset.M => "Preset M — Performance default",
        DlssPreset.Latest => "Latest — always use newest preset",
        _ => preset.ToString()
    };

    /// <summary>All user-selectable presets in display order.</summary>
    public static readonly DlssPreset[] AllPresets =
        [DlssPreset.Default, DlssPreset.J, DlssPreset.K, DlssPreset.L, DlssPreset.M, DlssPreset.Latest];
}
