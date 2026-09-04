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

    // --- DLSSG (Frame Generation generator) MODE + MULTIPLIER ---
    // These are a SEPARATE setting family from the FG_OVERRIDE_ENABLE / FG_RENDER_PRESET pair
    // above. Enabling the FG override and picking a render preset does NOT pick the generator
    // mode (Fixed vs Dynamic) nor the frame multiplier (2x/3x/4x… i.e. MFG). The NVIDIA App's
    // "DLSS Frame Generation = Dynamic, up to 6x, at max refresh rate" maps onto THESE IDs, which
    // the toolkit previously never wrote — so neither in-game toggles nor the toolkit could switch
    // Fixed/Dynamic or change the multiplier. IDs from NVIDIA's NvApiDriverSettings.h.

    /// <summary>NGX_DLSSG_MODE_ID — DLSSG generator mode (Disabled/Off/On/Auto/Dynamic).</summary>
    public const uint DLSSG_MODE = 0x10308298;

    /// <summary>
    /// NGX_DLSSG_MULTI_FRAME_COUNT_ID — FIXED multi-frame generated-frame count (0=off, 1..15).
    /// This is generated frames per real frame; total displayed multiplier = count + 1
    /// (count 1 = 2x, count 2 = 3x, … count 5 = 6x). Used when the mode is Fixed (On).
    /// </summary>
    public const uint DLSSG_MULTI_FRAME_COUNT = 0x104D6667;

    /// <summary>
    /// NGX_DLSSG_DYNAMIC_MULTI_FRAME_COUNT_MAX_ID — DYNAMIC mode cap on generated-frame count
    /// (0=off/unbounded by this knob, otherwise the max generated frames the driver may add).
    /// Same count→multiplier relationship as the fixed count (count + 1 = multiplier). Used when
    /// the mode is Dynamic.
    /// </summary>
    public const uint DLSSG_DYNAMIC_MULTI_FRAME_COUNT_MAX = 0x10562D0F;

    /// <summary>
    /// NGX_DLSSG_DYNAMIC_TARGET_FRAME_RATE_ID — dynamic-mode target FPS the generator aims at.
    /// 0 = disabled, 1..0x00FFFFFF = explicit target FPS, 0x01000000 = AUTO ("match max refresh
    /// rate"). The NVIDIA App "at max refresh rate" option is AUTO.
    /// </summary>
    public const uint DLSSG_DYNAMIC_TARGET_FRAME_RATE = 0x10CF4125;

    /// <summary>DLSSG dynamic target frame rate sentinel for AUTO ("match max refresh rate").</summary>
    public const uint DLSSG_DYNAMIC_TARGET_FRAME_RATE_AUTO = 0x01000000;
}

/// <summary>
/// DLSSG (DLSS Frame Generation generator) mode. Distinct from the FG override ENABLE flag:
/// the override flag turns FG on at the driver level; this picks HOW it generates frames.
/// uint-backed to match the DRS values directly (Enum.IsDefined requires matching underlying type).
/// Values are from NVIDIA's NvApiDriverSettings.h EValues_NGX_DLSSG_MODE.
/// </summary>
public enum DlssgMode : uint
{
    /// <summary>NGX_DLSSG_MODE_DISABLED — no DLSSG mode override written (driver/app default).</summary>
    Disabled = 0,

    /// <summary>NGX_DLSSG_MODE_OFF — explicitly off.</summary>
    Off = 1,

    /// <summary>NGX_DLSSG_MODE_ON — fixed frame generation (use the fixed multi-frame count).</summary>
    On = 2,

    /// <summary>NGX_DLSSG_MODE_AUTO — driver decides.</summary>
    Auto = 3,

    /// <summary>NGX_DLSSG_MODE_DYNAMIC — dynamic frame generation (use the dynamic max count + target FPS).</summary>
    Dynamic = 4,
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

    /// <summary>
    /// DLSS-RR outcome label. Only letters with a documented meaning get prose — inventing
    /// quality claims NVIDIA never published is the IsOpsSupported trap in dropdown form.
    /// </summary>
    public static string GetRrDescription(DlssPreset preset) => preset switch
    {
        DlssPreset.Default => "Default (no override)",
        // E was the recommendation for pre-4.5 RR models and is kept labelled for anyone running
        // an older DLL, but it must not read as "recommended" while F is the default (v0.70) —
        // a dropdown that recommends the preset the app doesn't pick is the same two-answers
        // problem the default itself had.
        DlssPreset.E => "Preset E — older RR models (pre-310.7.128)",
        DlssPreset.F => "Preset F — recommended (DLSS 4.5 RR and newer)",
        DlssPreset.Latest => "Latest — always use newest preset",
        _ => $"Preset {preset}"
    };

    /// <summary>DLSS-FG outcome label; documented letters only, same rule as RR.</summary>
    public static string GetFgDescription(DlssPreset preset) => preset switch
    {
        DlssPreset.Default => "Default (no override)",
        DlssPreset.B => "Preset B — recommended (higher quality than A)",
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

    /// <summary>DLSS-RR (Ray Reconstruction) presets — full A–M range. Recommended default: F.</summary>
    public static readonly DlssPreset[] RayReconstructionPresets =
        [DlssPreset.Default, .. LettersAtoM, DlssPreset.Latest];

    /// <summary>DLSS-FG (Frame Generation) presets — full A–M range. Recommended default: B.</summary>
    public static readonly DlssPreset[] FrameGenerationPresets =
        [DlssPreset.Default, .. LettersAtoM, DlssPreset.Latest];

    /// <summary>Recommended default for DLSS-SR.</summary>
    public const DlssPreset SuperResolutionDefault = DlssPreset.L;

    /// <summary>
    /// Recommended default for DLSS-RR.
    ///
    /// F, not E (v0.70). Every RR build this toolkit installs is 310.7.128 or newer, and on those
    /// builds Preset E does not engage the model at all — see the PresetVersionRules row below,
    /// which has said so since the rule table was added. The constant defaulted to E anyway, so a
    /// fresh install and every Reset handed the user a preset that silently does nothing until
    /// they take the version-rule prompt. The rule and the default now agree.
    /// </summary>
    public const DlssPreset RayReconstructionDefault = DlssPreset.F;

    /// <summary>Recommended default for DLSS-FG (B is higher quality than A).</summary>
    public const DlssPreset FrameGenerationDefault = DlssPreset.B;

    // --- Version-gated preset recommendations ------------------------------------------------

    /// <summary>
    /// A rule of the form "once component X reaches version V, its recommended preset becomes P".
    /// </summary>
    /// <param name="DllName">Canonical DLL whose installed version is tested, e.g. nvngx_dlssd.dll.</param>
    /// <param name="MinVersion">Inclusive lower bound, dotted, e.g. "310.7.128".</param>
    /// <param name="Preset">Preset to recommend at or above <paramref name="MinVersion"/>.</param>
    /// <param name="Reason">One line shown to the user explaining why.</param>
    public readonly record struct PresetVersionRule(
        string DllName,
        string MinVersion,
        DlssPreset Preset,
        string Reason);

    /// <summary>
    /// Version-gated preset recommendations, newest-first per component.
    ///
    /// WHY A TABLE. NVIDIA ties new preset letters to new model builds — the letter that is correct
    /// today is wrong for an older DLL and vice versa, so a single constant default cannot be right
    /// for both. Encoding it as data means the next model's requirement is one row, not a code
    /// change, and the app can explain the recommendation instead of silently changing a dropdown.
    ///
    /// PROVENANCE of the DLSS 4.5 / Preset F row: confirmed by first-party testing on Andrew's
    /// Windows machine — Ray Reconstruction 310.7.128 required Preset F to engage; Preset E did not
    /// work. It matches the letter NVIDIA's own materials attach to the 4.5 RR model, but the
    /// requirement itself is OBSERVED, not documented, which is why this is a suggestion the user
    /// confirms rather than a forced write.
    /// </summary>
    public static readonly PresetVersionRule[] PresetVersionRules =
    [
        new("nvngx_dlssd.dll", "310.7.128", DlssPreset.F,
            "DLSS 4.5 Ray Reconstruction (310.7.128+) needs Preset F — Preset E does not engage the new model."),
    ];

    /// <summary>
    /// Returns the rule that applies to <paramref name="dllName"/> at <paramref name="installedVersion"/>,
    /// or null when no rule matches.
    ///
    /// BOUNDARY CORRECTNESS. The comparison is "installed >= MinVersion", and it must be true at
    /// EXACTLY MinVersion — the whole point of the DLSS 4.5 row is the build Andrew actually tested,
    /// 310.7.128. Two ways of writing this were tried and both were wrong:
    ///   * <c>version == MinVersion || IsNewer(version, MinVersion)</c> — string equality cannot
    ///     match "310.7.128.0" against "310.7.128", so the exact target version fell through.
    ///   * <c>!IsNewer(MinVersion, version)</c> — returns TRUE for "Unknown"/"N/A"/garbage, because
    ///     an unparseable comparison is false in both directions. That recommends a preset off a
    ///     version we could not read.
    /// So both operands are parsed first and unparseable input returns null (no recommendation).
    /// </summary>
    public static PresetVersionRule? FindPresetRule(string? dllName, string? installedVersion)
    {
        if (string.IsNullOrWhiteSpace(dllName))
            return null;

        var installed = ParseVersionOrNull(installedVersion);
        if (installed == null)
            return null;

        foreach (var rule in PresetVersionRules)
        {
            if (!string.Equals(rule.DllName, dllName, StringComparison.OrdinalIgnoreCase))
                continue;

            var min = ParseVersionOrNull(rule.MinVersion);
            if (min == null)
                continue;

            if (installed >= min)
                return rule;
        }

        return null;
    }

    /// <summary>
    /// Parses a DLL version to a 4-part <see cref="Version"/>, or null when it is absent or not a
    /// version at all ("Unknown", "N/A", garbage). Accepts the comma form FileVersionInfo can
    /// produce ("310.7,128,0"), matching DllVersionReader's normalization.
    /// </summary>
    private static Version? ParseVersionOrNull(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var text = raw.Trim();
        if (text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("N/A", StringComparison.OrdinalIgnoreCase))
            return null;

        var parts = text.Replace(',', '.').Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return null;

        var nums = new int[4];
        for (var i = 0; i < 4; i++)
        {
            if (i >= parts.Length)
            {
                nums[i] = 0;
                continue;
            }
            if (!int.TryParse(parts[i], out var n))
                return null;
            nums[i] = n;
        }

        return new Version(nums[0], nums[1], nums[2], nums[3]);
    }

    /// <summary>
    /// All user-selectable SR presets in display order. Retained for backward compatibility
    /// with existing bindings; equal to <see cref="SuperResolutionPresets"/>.
    /// </summary>
    public static readonly DlssPreset[] AllPresets = SuperResolutionPresets;

    // --- DLSSG (Frame Generation generator) mode + multiplier ---

    /// <summary>User-selectable DLSSG modes in display order. Disabled = leave the mode knob untouched.</summary>
    public static readonly DlssgMode[] FrameGenModes =
        [DlssgMode.Disabled, DlssgMode.Off, DlssgMode.On, DlssgMode.Auto, DlssgMode.Dynamic];

    /// <summary>Recommended default DLSSG mode (Dynamic — adapts the multiplier to hit the target FPS).</summary>
    public const DlssgMode FrameGenModeDefault = DlssgMode.Dynamic;

    /// <summary>Human-readable label for a DLSSG mode.</summary>
    public static string GetModeLabel(DlssgMode mode) => mode switch
    {
        DlssgMode.Disabled => "Don't change",
        DlssgMode.Off => "Off",
        DlssgMode.On => "Fixed",
        DlssgMode.Auto => "Auto",
        DlssgMode.Dynamic => "Dynamic",
        _ => mode.ToString()
    };

    /// <summary>
    /// User-selectable frame multipliers (the "Nx" the NVIDIA App shows). 2x..6x today;
    /// the underlying field allows more (count up to 15 = 16x) but consumer MFG tops out lower.
    /// </summary>
    public static readonly int[] FrameGenMultipliers = [2, 3, 4, 5, 6];

    /// <summary>
    /// Recommended default multiplier when a fixed/dynamic-cap value is needed (6x — matches the
    /// NVIDIA App's "Dynamic, up to 6x, at max refresh rate" default; was 4x before v0.0.38).
    /// </summary>
    public const int FrameGenMultiplierDefault = 6;

    /// <summary>Minimum valid frame multiplier (2x = 1 generated frame).</summary>
    public const int FrameGenMultiplierMin = 2;

    /// <summary>Maximum frame multiplier the underlying count field allows (count 15 → 16x).</summary>
    public const int FrameGenMultiplierMax = 16;

    /// <summary>Label for a multiplier dropdown entry ("4x").</summary>
    public static string GetMultiplierLabel(int multiplier) => $"{multiplier}x";

    /// <summary>
    /// Converts a user-facing multiplier ("Nx") to the DRS generated-frame COUNT (N-1).
    /// 2x→1, 3x→2, … 6x→5. Clamped to the valid 1..15 count range.
    /// </summary>
    public static uint MultiplierToFrameCount(int multiplier)
    {
        if (multiplier < FrameGenMultiplierMin) multiplier = FrameGenMultiplierMin;
        if (multiplier > FrameGenMultiplierMax) multiplier = FrameGenMultiplierMax;
        return (uint)(multiplier - 1);
    }

    /// <summary>Inverse of <see cref="MultiplierToFrameCount"/>: count N → multiplier N+1.</summary>
    public static int FrameCountToMultiplier(uint count) => (int)count + 1;
}
