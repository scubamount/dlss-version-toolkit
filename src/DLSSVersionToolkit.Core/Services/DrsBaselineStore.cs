namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using DLSSVersionToolkit.Core.Models;

/// <summary>
/// What the toolkit's DRS (driver profile) settings looked like on each profile BEFORE the toolkit
/// first wrote to it (v0.77). Before this, Reset wrote "override off" (0) to SR/RR/FG on the base
/// profile and every game profile: a per-game preset the user had set in the NVIDIA App was
/// switched off, and every profile was left carrying explicit user-modified entries.
///
/// Capture is lazy: <see cref="PresetOverrideService"/> records a profile's current values for
/// <see cref="ManagedSettingIds"/> right before its first write to that profile, so the cost is
/// paid once per profile and only for profiles the toolkit actually touches.
///
/// Trust: a capture is only an "original" when no earlier toolkit build could have written the
/// profile first. <see cref="Trusted"/> is true only for a baseline created on the same launch as
/// the first-ever <see cref="OverrideBaseline"/> (a fresh install). Upgrades from v0.76 or earlier
/// start untrusted, and Reset then returns every managed setting to NVIDIA's default instead.
/// </summary>
public sealed class DrsBaseline
{
    public DateTime CreatedAt { get; set; }

    /// <summary>True when every captured value predates any toolkit write (see class remarks).</summary>
    public bool Trusted { get; set; }

    /// <summary>
    /// Profile name → setting ID → the user value set on that profile at capture, or null when the
    /// profile used NVIDIA's default (setting absent, predefined, or inherited from another profile).
    /// The base profile is stored under <see cref="DrsBaselineStore.BaseProfileKey"/>.
    /// </summary>
    public Dictionary<string, Dictionary<uint, uint?>> Profiles { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One setting to put back on a profile: a user value to write, or null = NVIDIA default.</summary>
public readonly record struct DrsRestoreAction(uint SettingId, uint? Value);

public sealed class DrsBaselineStore
{
    public const string FileName = "drs-baseline.json";

    /// <summary>Key for the base profile, which has no stable user-facing name.</summary>
    public const string BaseProfileKey = "<base>";

    /// <summary>Every DRS setting the toolkit writes. Capture and restore both use this one list.</summary>
    public static readonly IReadOnlyList<uint> ManagedSettingIds = new[]
    {
        DlssPresetSettingIds.SR_OVERRIDE_ENABLE,
        DlssPresetSettingIds.SR_RENDER_PRESET,
        DlssPresetSettingIds.RR_OVERRIDE_ENABLE,
        DlssPresetSettingIds.RR_RENDER_PRESET,
        DlssPresetSettingIds.FG_OVERRIDE_ENABLE,
        DlssPresetSettingIds.FG_RENDER_PRESET,
        DlssPresetSettingIds.DLSSG_MODE,
        DlssPresetSettingIds.DLSSG_MULTI_FRAME_COUNT,
        DlssPresetSettingIds.DLSSG_DYNAMIC_MULTI_FRAME_COUNT_MAX,
        DlssPresetSettingIds.DLSSG_DYNAMIC_TARGET_FRAME_RATE,
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _lock = new();
    private DrsBaseline? _cached;

    public DrsBaselineStore(string? appDataRoot = null)
    {
        AppDataRoot = appDataRoot ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DLSSVersionToolkit");
        BaselinePath = System.IO.Path.Combine(AppDataRoot, FileName);
    }

    public string AppDataRoot { get; }
    public string BaselinePath { get; }

    /// <summary>
    /// True when no earlier build can have written DRS settings here: neither the profile index
    /// (written by every apply that reaches a game profile since v0.0.39) nor the v0.76 Reset
    /// baseline exists. Must be evaluated BEFORE this launch creates either file. Conservative by
    /// design: any sign of a previous install means untrusted, and Reset then uses NVIDIA defaults.
    /// </summary>
    public bool LooksLikeFreshInstall() =>
        !EarlierInstallMarkers.Any(name => File.Exists(System.IO.Path.Combine(AppDataRoot, name)));

    /// <summary>
    /// Files only an earlier launch creates. settings.json has carried applied presets since
    /// v0.0.38, before the profile index existed (v0.0.39); overrides.json is written by local
    /// imports. Any one present = untrusted.
    /// </summary>
    public static readonly IReadOnlyList<string> EarlierInstallMarkers = new[]
    {
        "profile-index.json",
        OverrideResetService.BaselineFileName,
        "settings.json",
        "settings.previous.json",
        OverrideManifestService.ManifestFileName,
    };

    public bool Exists => File.Exists(BaselinePath);

    /// <summary>
    /// Creates the baseline file if none exists. <paramref name="trusted"/> must be true only when
    /// the toolkit has never written DRS settings on this machine (fresh install). Idempotent.
    /// Returns false when a baseline already existed. Throws <see cref="IOException"/> when the
    /// file cannot be written: a trusted baseline that only lives in memory is lost on exit, and
    /// the next launch can no longer tell a fresh install from one the toolkit already changed.
    /// </summary>
    public bool EnsureCreated(bool trusted)
    {
        lock (_lock)
        {
            if (File.Exists(BaselinePath)) return false;
            _cached = new DrsBaseline { CreatedAt = DateTime.UtcNow, Trusted = trusted };
            if (!Save(_cached))
                throw new IOException($"Could not write the DRS baseline to {BaselinePath}.");
            return true;
        }
    }

    /// <summary>The baseline, or a new untrusted one when the file is missing or unreadable.</summary>
    public DrsBaseline Load()
    {
        lock (_lock)
        {
            if (_cached != null) return _cached;
            try
            {
                if (File.Exists(BaselinePath))
                {
                    var loaded = JsonSerializer.Deserialize<DrsBaseline>(File.ReadAllText(BaselinePath));
                    if (loaded != null)
                    {
                        loaded.Profiles = new Dictionary<string, Dictionary<uint, uint?>>(
                            loaded.Profiles ?? new(), StringComparer.Ordinal);
                        return _cached = loaded;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                // Unreadable = unknown originals: untrusted, so Reset falls back to NVIDIA defaults.
                Debug.WriteLine($"DrsBaselineStore: baseline unreadable, treating as untrusted: {ex.Message}");
            }
            return _cached = new DrsBaseline { CreatedAt = DateTime.UtcNow, Trusted = false };
        }
    }

    /// <summary>True when this profile already has a captured entry (so it must not be re-captured).</summary>
    public bool HasProfile(string profileKey)
    {
        lock (_lock) return Load().Profiles.ContainsKey(profileKey);
    }

    /// <summary>
    /// Records a profile's pre-write values. Never overwrites an existing entry: a second capture
    /// would read the toolkit's own writes and record them as original.
    /// </summary>
    public void RecordIfAbsent(string profileKey, IReadOnlyDictionary<uint, uint?> values)
    {
        lock (_lock)
        {
            var baseline = Load();
            if (baseline.Profiles.ContainsKey(profileKey)) return;
            baseline.Profiles[profileKey] = new Dictionary<uint, uint?>(values);
        }
    }

    /// <summary>
    /// Persists the in-memory baseline. Call once per apply, BEFORE the driver session save, and
    /// abort the save when this returns false: driver writes without persisted captures would be
    /// re-captured on the next launch as if they were the user's originals.
    /// </summary>
    public bool Flush()
    {
        lock (_lock)
        {
            return _cached == null || Save(_cached);
        }
    }

    /// <summary>
    /// After an untrusted Reset every managed setting is back at NVIDIA's default, which is a true
    /// original state, so later captures can be trusted. Clears the captures and marks it trusted.
    /// </summary>
    /// <returns>False when the file could not be written; the old captures are then still on disk.</returns>
    public bool MarkCleanAndTrusted()
    {
        lock (_lock)
        {
            var fresh = new DrsBaseline { CreatedAt = DateTime.UtcNow, Trusted = true };
            if (!Save(fresh))
            {
                _cached = null; // stay consistent with disk; the caller reports the failure
                return false;
            }
            _cached = fresh;
            return true;
        }
    }

    /// <summary>
    /// What Reset writes on one profile. Trusted + captured: put each captured value back (null =
    /// NVIDIA default). Trusted + never captured: the toolkit never wrote this profile, so leave it
    /// alone (empty list). Untrusted: return every managed setting to NVIDIA's default.
    /// </summary>
    public static IReadOnlyList<DrsRestoreAction> PlanRestore(DrsBaseline baseline, string profileKey)
    {
        if (baseline.Trusted)
        {
            if (!baseline.Profiles.TryGetValue(profileKey, out var captured))
                return Array.Empty<DrsRestoreAction>();
            return ManagedSettingIds
                .Select(id => new DrsRestoreAction(id, captured.TryGetValue(id, out var v) ? v : null))
                .ToList();
        }
        return ManagedSettingIds.Select(id => new DrsRestoreAction(id, null)).ToList();
    }

    private bool Save(DrsBaseline baseline)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(BaselinePath);
            if (dir != null) Directory.CreateDirectory(dir);
            var tmp = BaselinePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(baseline, JsonOptions));
            File.Move(tmp, BaselinePath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"DrsBaselineStore: save failed: {ex.Message}");
            return false;
        }
    }
}
