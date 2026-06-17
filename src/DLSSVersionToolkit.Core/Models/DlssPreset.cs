namespace DLSSVersionToolkit.Core.Models;

/// <summary>
/// DLSS render preset override values for the NVIDIA DRS (Driver Registry Settings).
/// The letter → value mapping is identical across DLSS-SR, DLSS-RR and DLSS-FG per NVIDIA's
/// NvApiDriverSettings.h (A=1, B=2, … M=13), so this single enum is reused for all three
/// underlying values ARE the DRS preset values. The enum is uint-backed so it matches the
/// uint DRS values directly (Enum.IsDefined requires the boxed value's type to equal the
/// enum's underlying type).
/// </summary>
public enum DlssPreset : uint
{
    /// <summary>No override — use driver default.</summary>
    Default = 0x00000000,

    /// <summary>Preset A.</summary>
    A = 0x00000001,
    /// <summary>Preset B.</summary>
    B = 0x00000002,
    /// <summary>Preset C.</summary>
    C = 0x00000003,
    /// <summary>Preset D.</summary>
    D = 0x00000004,
    /// <summary>Preset E.</summary>
    E = 0x00000005,
    /// <summary>Preset F.</summary>
    F = 0x00000006,
    /// <summary>Preset G.</summary>
    G = 0x00000007,
    /// <summary>Preset H.</summary>
    H = 0x00000008,
    /// <summary>Preset I.</summary>
    I = 0x00000009,
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
    // Each feature has its OWN preset-selection ID. They must NOT be cross-assigned — e.g.
    // mirroring the SR letter onto RR (a bug fixed in v0.0.35) gives RR the wrong preset.

    /// <summary>NGX_DLSS_SR_OVERRIDE_RENDER_PRESET_SELECTION_ID — DLSS-SR preset selection.</summary>
    public const uint SR_RENDER_PRESET = 0x10E41DF3;

    /// <summary>NGX_DLSS_RR_OVERRIDE_RENDER_PRESET_SELECTION_ID — DLSS-RR preset selection.</summary>
    public const uint RR_RENDER_PRESET = 0x10E41DF7;

    /// <summary>NGX_DLSS_FG_OVERRIDE_RENDER_PRESET_SELECTION_ID — DLSS-FG preset selection.</summary>
    public const uint FG_RENDER_PRESET = 0x10E41DF1;
}

/// <summary>
/// Display metadata and per-feature preset lists for the three DLSS override features.
/// </summary>
public static class DlssPresetDisplay
{
    /// <summary>Gets the human-readable description for a DLSS-SR (Super Resolution) preset.</summary>
    public static string GetDescription(DlssPreset preset) => preset switch
    {
        DlssPreset.Default => "Default (no override)",
        DlssPreset.J => "Preset J — less ghosting, more flicker",
        DlssPreset.K => "Preset K — DLAA/Balanced/Quality default",
        DlssPreset.L => "Preset L — Ultra Performance default",
        DlssPreset.M => "Preset M — Performance default",
        DlssPreset.Latest => "Latest — always use newest preset",
        _ => $"Preset {preset}"
    };

    /// <summary>Short label for a preset in a compact dropdown ("Default", "Latest", or "Preset X").</summary>
    public static string GetShortLabel(DlssPreset preset) => preset switch
    {
        DlssPreset.Default => "Default",
        DlssPreset.Latest => "Latest",
        _ => $"Preset {preset}"
    };

    private static readonly DlssPreset[] LettersAtoM =
    [
        DlssPreset.A, DlssPreset.B, DlssPreset.C, DlssPreset.D, DlssPreset.E, DlssPreset.F,
        DlssPreset.G, DlssPreset.H, DlssPreset.I, DlssPreset.J, DlssPreset.K, DlssPreset.L, DlssPreset.M
    ];

    /// <summary>
    /// DLSS-SR (Super Resolution) presets. Historically the only knob the app exposed;
    /// recommended default is L. Kept as the original curated short list (Default, J–M, Latest)
    /// plus the full A–M range for power users.
    /// </summary>
    public static readonly DlssPreset[] SuperResolutionPresets =
        [DlssPreset.Default, .. LettersAtoM, DlssPreset.Latest];

    /// <summary>DLSS-RR (Ray Reconstruction) presets — full A–M range. Recommended default: E.</summary>
    public static readonly DlssPreset[] RayReconstructionPresets =
        [DlssPreset.Default, .. LettersAtoM, DlssPreset.Latest];

    /// <summary>DLSS-FG (Frame Generation) presets — full A–M range. Recommended default: B.</summary>
    public static readonly DlssPreset[] FrameGenerationPresets =
        [DlssPreset.Default, .. LettersAtoM, DlssPreset.Latest];

    /// <summary>Recommended default for DLSS-SR.</summary>
    public const DlssPreset SuperResolutionDefault = DlssPreset.L;

    /// <summary>Recommended default for DLSS-RR (best quality per NVIDIA's current models).</summary>
    public const DlssPreset RayReconstructionDefault = DlssPreset.E;

    /// <summary>Recommended default for DLSS-FG (B is higher quality than A).</summary>
    public const DlssPreset FrameGenerationDefault = DlssPreset.B;

    /// <summary>
    /// All user-selectable SR presets in display order. Retained for backward compatibility
    /// with existing bindings; equal to <see cref="SuperResolutionPresets"/>.
    /// </summary>
    public static readonly DlssPreset[] AllPresets = SuperResolutionPresets;
}
