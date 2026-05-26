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
/// </summary>
public static class DlssPresetSettingIds
{
    /// <summary>DLSS-SR (Super Resolution) preset override setting ID.</summary>
    public const uint SR_RENDER_PRESET = 0x10E41DF3;

    /// <summary>DLSS-RR (Ray Reconstruction) preset override setting ID.</summary>
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
