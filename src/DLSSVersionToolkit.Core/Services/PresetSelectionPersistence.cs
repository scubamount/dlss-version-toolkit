namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;

/// <summary>
/// Maps persisted preset-selection strings (AppSettings) to/from the strongly-typed enums.
/// Selections are stored as enum NAMES ("K", "Latest", "Dynamic") rather than raw uints so a
/// hand-edited settings.json stays readable and an unknown/corrupt value degrades to null
/// (caller falls back to the recommended default) instead of failing deserialization.
/// New in v0.0.38 — before this, no preset selection survived an app restart (the known
/// "resets to Preset L on relaunch" limitation).
/// </summary>
public static class PresetSelectionPersistence
{
    /// <summary>Serializes a preset for storage ("K", "Latest"). Null-safe.</summary>
    public static string Serialize(DlssPreset? preset) => preset?.ToString() ?? "";

    /// <summary>Serializes a DLSSG mode for storage ("Dynamic", "On").</summary>
    public static string Serialize(DlssgMode mode) => mode.ToString();

    /// <summary>
    /// Parses a stored preset name. Returns null for empty/unknown values so the caller can
    /// keep its default. Numeric strings are accepted only when they map to a defined value
    /// (guards against garbage like "999").
    /// </summary>
    public static DlssPreset? ParsePreset(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return null;
        return Enum.TryParse<DlssPreset>(stored.Trim(), ignoreCase: true, out var preset)
               && Enum.IsDefined(preset)
            ? preset
            : null;
    }

    /// <summary>Parses a stored DLSSG mode name. Null for empty/unknown (keep default).</summary>
    public static DlssgMode? ParseMode(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return null;
        return Enum.TryParse<DlssgMode>(stored.Trim(), ignoreCase: true, out var mode)
               && Enum.IsDefined(mode)
            ? mode
            : null;
    }

    /// <summary>
    /// Validates a stored FG multiplier. Returns null when unset (0) or outside the valid
    /// 2x..16x range, so the caller keeps the recommended default.
    /// </summary>
    public static int? ParseMultiplier(int stored)
    {
        if (stored < DlssPresetDisplay.FrameGenMultiplierMin ||
            stored > DlssPresetDisplay.FrameGenMultiplierMax)
            return null;
        return stored;
    }

    /// <summary>Writes the current selections onto a settings object (caller persists it).</summary>
    public static void ApplyTo(AppSettings settings,
        DlssPreset? srPreset, DlssPreset rrPreset, DlssPreset fgPreset,
        DlssgMode fgMode, int fgMultiplier)
    {
        settings.SelectedSrPreset = Serialize(srPreset);
        settings.SelectedRrPreset = Serialize(rrPreset);
        settings.SelectedFgPreset = Serialize(fgPreset);
        settings.SelectedFgMode = Serialize(fgMode);
        settings.SelectedFgMultiplier = fgMultiplier;
    }
}
