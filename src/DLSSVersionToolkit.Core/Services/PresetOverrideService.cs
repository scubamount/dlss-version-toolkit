using System.Diagnostics;
using System.Runtime.Versioning;
using DLSSVersionToolkit.Core.Models;
using NvAPIWrapper;
using NvAPIWrapper.DRS;
using NvAPIWrapper.Native.DRS;
using NvAPIWrapper.Native.Exceptions;
using NvAPIWrapper.Native.General;

namespace DLSSVersionToolkit.Core.Services;

/// <summary>
/// Result of a preset override operation.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="CurrentPreset">The currently applied preset (null if unavailable).</param>
/// <param name="ErrorMessage">Error message if the operation failed.</param>
/// <param name="PermissionIssue">True if the failure was due to insufficient privileges (not running as admin).</param>
public sealed record PresetOverrideResult(
    bool Success,
    DlssPreset? CurrentPreset,
    string? ErrorMessage,
    bool PermissionIssue = false,
    int ProfilesUpdated = 0,
    int GameProfilesUpdated = 0,
    long ElapsedMs = 0,
    long EnumerateMs = 0,
    long WriteMs = 0,
    long SaveMs = 0,
    bool UsedIndex = false,
    /// <summary>Profiles that failed the per-profile write and were skipped. Debug-only before
    /// v0.0.57 — partial success reported itself as flat success.</summary>
    int ProfilesSkipped = 0);

/// <summary>
/// Options controlling which DLSS feature overrides are enabled when applying a preset.
/// </summary>
public sealed record PresetApplyOptions
{
    /// <summary>Enable the DLSS-SR (Super Resolution) override and set its render preset. Always true in practice.</summary>
    public bool EnableSuperResolution { get; init; } = true;

    /// <summary>Also enable the DLSS-RR (Ray Reconstruction / "NR" denoiser) DLL override.</summary>
    public bool EnableRayReconstruction { get; init; } = true;

    /// <summary>Also enable the DLSS-FG (Frame Generation) DLL override.</summary>
    public bool EnableFrameGeneration { get; init; } = true;

    /// <summary>
    /// DLSS-RR (Ray Reconstruction) render preset. Has its OWN preset selection, independent of
    /// SR — do NOT reuse the SR letter here. Null = derive from the SR preset is NOT done;
    /// defaults to the recommended RR preset (F). Set to Default to clear the RR preset selection.
    /// </summary>
    public DlssPreset RayReconstructionPreset { get; init; } = DlssPresetDisplay.RayReconstructionDefault;

    /// <summary>
    /// DLSS-FG (Frame Generation) render preset. Independent of SR/RR. Defaults to the
    /// recommended FG preset (B). Set to Default to clear the FG preset selection.
    /// </summary>
    public DlssPreset FrameGenerationPreset { get; init; } = DlssPresetDisplay.FrameGenerationDefault;

    /// <summary>
    /// DLSSG generator MODE (Fixed/Dynamic/Auto/Off). Distinct from <see cref="EnableFrameGeneration"/>:
    /// that flag turns the FG override on; this picks HOW frames are generated. Default = Disabled,
    /// which leaves the mode knob untouched (back-compat: existing callers don't change FG mode).
    /// Set to On/Dynamic/Auto/Off to actually write NGX_DLSSG_MODE.
    /// </summary>
    public DlssgMode FrameGenerationMode { get; init; } = DlssgMode.Disabled;

    /// <summary>
    /// Frame multiplier (the "Nx" shown in the NVIDIA App: 2x..6x). Written as the DRS
    /// generated-frame count (multiplier - 1) into either the FIXED count (mode On) or the
    /// DYNAMIC max count (mode Dynamic). Ignored when the mode is Disabled/Off/Auto.
    /// </summary>
    public int FrameGenerationMultiplier { get; init; } = DlssPresetDisplay.FrameGenMultiplierDefault;

    /// <summary>
    /// In Dynamic mode, the target FPS the generator aims at. Null = AUTO ("match max refresh
    /// rate", the NVIDIA App default). A positive value sets an explicit FPS target. Ignored
    /// unless the mode is Dynamic.
    /// </summary>
    public int? FrameGenerationDynamicTargetFps { get; init; } = null;

    /// <summary>
    /// Apply to every game profile (not just the global/base profile). This is what
    /// actually changes in-game behavior for games that have their own DRS profile.
    /// </summary>
    public bool ApplyToAllGameProfiles { get; init; } = true;
}

/// <summary>
/// Reads and writes DLSS render preset overrides via the NVIDIA DRS (Driver Registry Settings) API.
/// Requires nvapi64.dll from the NVIDIA driver and admin privileges for writing.
/// </summary>
[SupportedOSPlatform("windows")]
public interface IPresetOverrideService
{
    /// <summary>
    /// Reads the current global DLSS-SR preset override from the NVIDIA driver.
    /// Returns Default if no override is set.
    /// </summary>
    Task<PresetOverrideResult> GetCurrentPresetAsync(CancellationToken ct = default);

    /// <summary>
    /// Applies a DLSS preset override across the global profile and (by default) every
    /// game profile, enabling the SR override ("Custom") plus optionally RR and FG
    /// overrides. Requires admin privileges. Reports (done, total) game-profile progress.
    /// </summary>
    Task<PresetOverrideResult> ApplyPresetAsync(DlssPreset preset, PresetApplyOptions? options = null, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Rebuilds the persisted game-profile index (full driver-profile scan) without writing
    /// any settings. GameProfilesUpdated carries the indexed count.
    /// </summary>
    Task<PresetOverrideResult> RebuildProfileIndexAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates the DRS baseline file if none exists (v0.77). <paramref name="trusted"/> = the
    /// toolkit has never written driver settings on this machine. Call once at startup, before
    /// any apply.
    /// </summary>
    void EnsureDrsBaseline(bool trusted);

    /// <summary>
    /// True when Reset can restore each profile's own pre-toolkit values. False (upgrade from
    /// v0.76 or earlier) means Reset returns every managed setting to NVIDIA's default on every
    /// game profile, including values set in the NVIDIA App — the Reset dialog must say so.
    /// </summary>
    bool DrsBaselineTrusted { get; }

    /// <summary>
    /// Reset (v0.77): puts every DRS setting the toolkit manages back to its pre-toolkit value on
    /// the base profile and the game profiles — the captured user value, or NVIDIA's default where
    /// there was none. Replaces the v0.76 behavior of writing "override off" everywhere.
    /// </summary>
    Task<PresetOverrideResult> RestoreBaselineAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the NVIDIA DRS API is available (i.e., NVIDIA drivers are installed).
    /// </summary>
    bool IsAvailable { get; }
}

[SupportedOSPlatform("windows")]
public sealed class PresetOverrideService : IPresetOverrideService
{
    private bool? _isAvailable;
    private readonly DrsBaselineStore _baseline;

    public PresetOverrideService(DrsBaselineStore? baseline = null) => _baseline = baseline ?? new DrsBaselineStore();

    public void EnsureDrsBaseline(bool trusted) => _baseline.EnsureCreated(trusted);

    public bool DrsBaselineTrusted => _baseline.Load().Trusted;

    public bool IsAvailable
    {
        get
        {
            _isAvailable ??= CheckAvailability();
            return _isAvailable.Value;
        }
    }

    public async Task<PresetOverrideResult> GetCurrentPresetAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            try
            {
                EnsureInitialized();
                using var session = DriverSettingsSession.CreateAndLoad();
                // Read the profile ApplyPresetAsync WRITES (BaseProfile). v0.75 and earlier read
                // CurrentGlobalProfile — a different profile whenever the user has a named global
                // profile selected — so right after applying Preset L the dashboard said
                // "Current: Default (no override)". Global profile is only the fallback.
                var profile = session.BaseProfile ?? session.CurrentGlobalProfile;
                if (profile is null)
                {
                    return new PresetOverrideResult(false, null, "Could not get global profile.");
                }

                // The preset selection is ignored by the driver unless the SR override is enabled
                // (ApplyToProfile writes both). A leftover selection with the enable flag off is
                // NOT an active override, and reporting it as one would be the opposite lie.
                var enabled = ReadUInt(profile, DlssPresetSettingIds.SR_OVERRIDE_ENABLE);
                if (enabled != DlssPresetSettingIds.OVERRIDE_ON)
                    return new PresetOverrideResult(true, DlssPreset.Default, null);

                var presetValue = ReadUInt(profile, DlssPresetSettingIds.SR_RENDER_PRESET);
                var preset = PresetFromValue(presetValue ?? 0);
                return new PresetOverrideResult(true, preset, null);
            }
            catch (NVIDIAApiException ex)
            {
                Debug.WriteLine($"PresetOverrideService: NVIDIA API error reading preset: {ex.Status}");
                var permissionIssue = ex.Status == Status.InvalidUserPrivilege;
                return new PresetOverrideResult(
                    false, null,
                    permissionIssue ? "Admin privileges required. Run as administrator." : $"NVIDIA API error: {ex.Status}",
                    permissionIssue);
            }
            catch (DllNotFoundException ex)
            {
                Debug.WriteLine($"PresetOverrideService: nvapi64.dll not found: {ex.Message}");
                _isAvailable = false;
                return new PresetOverrideResult(false, null, "NVIDIA driver not found (nvapi64.dll missing).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PresetOverrideService: Error reading preset: {ex.Message}");
                return new PresetOverrideResult(false, null, $"Error reading preset: {ex.Message}");
            }
        }, ct);
    }

    public async Task<PresetOverrideResult> ApplyPresetAsync(DlssPreset preset, PresetApplyOptions? options = null, IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        options ??= new PresetApplyOptions();
        return await Task.Run(() =>
        {
            var total = Stopwatch.StartNew();
            var enumerateMs = 0L;
            var writeMs = 0L;
            try
            {
                EnsureInitialized();
                using var session = DriverSettingsSession.CreateAndLoad();

                var presetValue = (uint)preset;
                // Per-feature (v0.77): each feature is on when ITS preset is not Default. Before,
                // the SR preset alone decided for all three: SR = Default switched RR and FG off
                // too, and SR = L with RR = Default wrote RR's enable ON with preset 0.
                bool enable = preset != DlssPreset.Default
                    || options.RayReconstructionPreset != DlssPreset.Default
                    || options.FrameGenerationPreset != DlssPreset.Default;
                int profilesUpdated = 0;
                int gameProfilesUpdated = 0;
                int profilesSkipped = 0;
                bool usedIndex = false;

                // 1) Base profile = the global default inherited by profiles that don't
                //    override these settings themselves.
                var sw = Stopwatch.StartNew();
                var baseProfile = session.BaseProfile;
                if (baseProfile is not null)
                {
                    // A capture that cannot be read must not fail the whole apply, and the profile
                    // must not be written without one: skip it and count the skip.
                    if (TryCaptureBeforeFirstWrite(baseProfile, DrsBaselineStore.BaseProfileKey))
                    {
                        ApplyToProfile(baseProfile, presetValue, options);
                        profilesUpdated++;
                    }
                    else profilesSkipped++;
                }
                writeMs += sw.ElapsedMilliseconds;

                // 2) Game profiles. Two paths (v0.0.39):
                //
                //    FAST PATH — a valid persisted index exists (see ProfileIndexStore).
                //    The index is the set of profile names with applications installed, i.e.
                //    exactly the shadow set: since v0.0.35 we write the override IDs to every
                //    such profile, so they no longer inherit from base and MUST be written
                //    directly. FindProfileByName per cached name skips the ~8000-profile
                //    GetProfileInfo filter scan that dominated apply time.
                //
                //    SLOW PATH — no/stale index. Full scan (pre-v0.0.39 behavior), and the
                //    surviving names are captured to (re)build the index as a side effect,
                //    so the slow path is paid at most once per driver version.
                if (options.ApplyToAllGameProfiles)
                {
                    var driverVersion = GetDriverVersionString();
                    var index = ProfileIndexStore.LoadValid(driverVersion);

                    if (index != null)
                    {
                        usedIndex = true;
                        var names = index.GameProfileNames;
                        sw.Restart();
                        for (int i = 0; i < names.Count; i++)
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                var profile = session.FindProfileByName(names[i]);
                                if (profile is null || !profile.IsValid)
                                    continue; // profile removed since indexing — harmless skip
                                CaptureBeforeFirstWrite(profile, names[i]);
                                ApplyToProfile(profile, presetValue, options);
                                profilesUpdated++;
                                gameProfilesUpdated++;
                            }
                            catch (NVIDIAApiException pex)
                            {
                                profilesSkipped++;
                                Debug.WriteLine($"PresetOverrideService: indexed profile '{names[i]}' skipped: {pex.Status}");
                            }
                            // ponytail: throttle UI marshaling — report every 25, not every profile
                            if (progress != null && (i % 25 == 0 || i == names.Count - 1))
                                progress.Report((i + 1, names.Count));
                        }
                        writeMs += sw.ElapsedMilliseconds;
                    }
                    else
                    {
                        var indexedNames = new List<string>();
                        sw.Restart();
                        var profiles = session.Profiles.ToList(); // single EnumProfiles call
                        enumerateMs = sw.ElapsedMilliseconds;

                        sw.Restart();
                        int done = 0;
                        foreach (var profile in profiles)
                        {
                            ct.ThrowIfCancellationRequested();
                            done++;
                            try
                            {
                                if (profile is null || !profile.IsValid)
                                    continue;

                                // Single GetProfileInfo-backed read per profile (cached in a local).
                                int appCount = profile.NumberOfApplications;
                                if (appCount <= 0)
                                    continue;

                                var profileName = profile.Name;
                                CaptureBeforeFirstWrite(profile, profileName);
                                ApplyToProfile(profile, presetValue, options);
                                profilesUpdated++;
                                gameProfilesUpdated++;
                                indexedNames.Add(profileName);
                            }
                            catch (NVIDIAApiException pex)
                            {
                                // Don't let one stubborn profile abort the whole sweep — but the
                                // skip is counted, not just logged (v0.0.57).
                                profilesSkipped++;
                                Debug.WriteLine($"PresetOverrideService: skipped a profile: {pex.Status}");
                            }
                            if (progress != null && (done % 250 == 0 || done == profiles.Count))
                                progress.Report((done, profiles.Count));
                        }
                        writeMs += sw.ElapsedMilliseconds;

                        // Rebuild the index from this scan so the next apply takes the fast path.
                        if (indexedNames.Count > 0)
                            ProfileIndexStore.Save(new ProfileIndex
                            {
                                DriverVersion = driverVersion,
                                IndexedAt = DateTime.UtcNow,
                                GameProfileNames = indexedNames
                            });
                    }
                }

                sw.Restart();
                // Persist captures BEFORE the driver save. Every capture holds pre-write values, so
                // one whose write never lands is harmless. The reverse order is not: a failed or
                // interrupted Save left in-memory captures unpersisted, the next apply re-captured
                // the profile AFTER this run's writes, and a trusted Reset wrote those back as
                // "originals".
                _baseline.Flush();
                session.Save();
                var saveMs = sw.ElapsedMilliseconds;
                total.Stop();

                Debug.WriteLine($"PresetOverrideService: Applied preset {preset} (0x{presetValue:X}) enable={enable} to {profilesUpdated} profile(s) ({gameProfilesUpdated} game) in {total.ElapsedMilliseconds}ms [enum {enumerateMs}ms, write {writeMs}ms, save {saveMs}ms, index={usedIndex}].");
                return new PresetOverrideResult(true, preset, null, false, profilesUpdated, gameProfilesUpdated,
                    total.ElapsedMilliseconds, enumerateMs, writeMs, saveMs, usedIndex, profilesSkipped);
            }
            catch (NVIDIAApiException ex)
            {
                Debug.WriteLine($"PresetOverrideService: NVIDIA API error writing preset: {ex.Status}");
                var permissionIssue = ex.Status == Status.InvalidUserPrivilege;
                return new PresetOverrideResult(
                    false, null,
                    permissionIssue ? "Admin privileges required. Run as administrator." : $"NVIDIA API error: {ex.Status}",
                    permissionIssue);
            }
            catch (DllNotFoundException ex)
            {
                Debug.WriteLine($"PresetOverrideService: nvapi64.dll not found: {ex.Message}");
                _isAvailable = false;
                return new PresetOverrideResult(false, null, "NVIDIA driver not found (nvapi64.dll missing).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PresetOverrideService: Error writing preset: {ex.Message}");
                return new PresetOverrideResult(false, null, $"Error writing preset: {ex.Message}");
            }
        }, ct);
    }

    public async Task<PresetOverrideResult> RebuildProfileIndexAsync(CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var total = Stopwatch.StartNew();
            try
            {
                EnsureInitialized();
                using var session = DriverSettingsSession.CreateAndLoad();

                var names = new List<string>();
                foreach (var profile in session.Profiles)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if (profile is null || !profile.IsValid)
                            continue;
                        if (profile.NumberOfApplications <= 0)
                            continue;
                        names.Add(profile.Name);
                    }
                    catch (NVIDIAApiException pex)
                    {
                        Debug.WriteLine($"RebuildProfileIndex: skipped a profile: {pex.Status}");
                    }
                }

                ProfileIndexStore.Save(new ProfileIndex
                {
                    DriverVersion = GetDriverVersionString(),
                    IndexedAt = DateTime.UtcNow,
                    GameProfileNames = names
                });

                total.Stop();
                Debug.WriteLine($"RebuildProfileIndex: {names.Count} game profiles indexed in {total.ElapsedMilliseconds}ms.");
                return new PresetOverrideResult(true, null, null, false, 0, names.Count, total.ElapsedMilliseconds);
            }
            catch (NVIDIAApiException ex)
            {
                var permissionIssue = ex.Status == Status.InvalidUserPrivilege;
                return new PresetOverrideResult(false, null,
                    permissionIssue ? "Admin privileges required. Run as administrator." : $"NVIDIA API error: {ex.Status}",
                    permissionIssue);
            }
            catch (Exception ex)
            {
                return new PresetOverrideResult(false, null, $"Error indexing profiles: {ex.Message}");
            }
        }, ct);
    }

    public async Task<PresetOverrideResult> RestoreBaselineAsync(IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var total = Stopwatch.StartNew();
            try
            {
                EnsureInitialized();
                using var session = DriverSettingsSession.CreateAndLoad();
                var baseline = _baseline.Load();
                int profilesUpdated = 0, gameProfilesUpdated = 0, profilesSkipped = 0;

                // Base profile first: it is what every non-overriding profile inherits.
                if (session.BaseProfile is { } baseProfile)
                {
                    if (RestoreProfile(baseProfile, DrsBaselineStore.PlanRestore(baseline, DrsBaselineStore.BaseProfileKey)))
                        profilesUpdated++;
                    else
                        profilesSkipped++;
                }

                // Trusted: only profiles the toolkit captured (it never wrote any other).
                // Untrusted: every game profile, since an older build may have written any of them.
                var names = baseline.Trusted
                    ? baseline.Profiles.Keys.Where(k => k != DrsBaselineStore.BaseProfileKey).ToList()
                    : GameProfileNames(session, ct);

                for (int i = 0; i < names.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var plan = DrsBaselineStore.PlanRestore(baseline, names[i]);
                    if (plan.Count == 0) continue;
                    try
                    {
                        var profile = session.FindProfileByName(names[i]);
                        if (profile is null || !profile.IsValid)
                            continue; // removed since capture (driver reinstall) — nothing to restore
                        if (RestoreProfile(profile, plan)) { profilesUpdated++; gameProfilesUpdated++; }
                        else profilesSkipped++;
                    }
                    catch (NVIDIAApiException pex)
                    {
                        profilesSkipped++;
                        Debug.WriteLine($"RestoreBaseline: profile '{names[i]}' skipped: {pex.Status}");
                    }
                    if (progress != null && (i % 25 == 0 || i == names.Count - 1))
                        progress.Report((i + 1, names.Count));
                }

                session.Save();

                // A clean reset leaves every touched profile in its original state (captured
                // values, or NVIDIA's default after an untrusted reset), so the old captures are
                // spent: clear them and trust fresh ones. Keeping them would let a later Reset
                // overwrite changes the user makes in the NVIDIA App after this one. A skipped
                // profile still carries toolkit values, so its capture (or distrust) must stay.
                if (profilesSkipped == 0)
                    _baseline.MarkCleanAndTrusted();

                total.Stop();
                return new PresetOverrideResult(true, null, null, false, profilesUpdated, gameProfilesUpdated,
                    total.ElapsedMilliseconds, ProfilesSkipped: profilesSkipped);
            }
            catch (NVIDIAApiException ex)
            {
                var permissionIssue = ex.Status == Status.InvalidUserPrivilege;
                return new PresetOverrideResult(false, null,
                    permissionIssue ? "Admin privileges required. Run as administrator." : $"NVIDIA API error: {ex.Status}",
                    permissionIssue);
            }
            catch (DllNotFoundException)
            {
                _isAvailable = false;
                return new PresetOverrideResult(false, null, "NVIDIA driver not found (nvapi64.dll missing).");
            }
            catch (Exception ex)
            {
                return new PresetOverrideResult(false, null, $"Error restoring driver settings: {ex.Message}");
            }
        }, ct);
    }

    /// <summary>
    /// Records this profile's managed values before the toolkit's first write to it. Skipped when
    /// the profile already has an entry, so the toolkit's own writes are never captured.
    /// </summary>
    private bool TryCaptureBeforeFirstWrite(DriverSettingsProfile profile, string profileKey)
    {
        try
        {
            CaptureBeforeFirstWrite(profile, profileKey);
            return true;
        }
        catch (NVIDIAApiException ex)
        {
            Debug.WriteLine($"PresetOverrideService: baseline capture for '{profileKey}' failed: {ex.Status}");
            return false;
        }
    }

    private void CaptureBeforeFirstWrite(DriverSettingsProfile profile, string profileKey)
    {
        if (_baseline.HasProfile(profileKey)) return;
        var isBase = profileKey == DrsBaselineStore.BaseProfileKey;
        var values = new Dictionary<uint, uint?>();
        foreach (var id in DrsBaselineStore.ManagedSettingIds)
            values[id] = ReadOwnUserValue(profile, id, isBase);
        _baseline.RecordIfAbsent(profileKey, values);
    }

    /// <summary>
    /// The value set by a user ON THIS profile, or null when the profile uses NVIDIA's default
    /// (setting absent, NVIDIA-predefined, or inherited from the base/global profile). Restoring
    /// null means RestoreSettingToDefault, which is exactly how such a profile looked before.
    /// </summary>
    private static uint? ReadOwnUserValue(DriverSettingsProfile profile, uint settingId, bool isBase)
    {
        try
        {
            var setting = profile.GetSetting(settingId);
            if (setting is null || setting.IsCurrentValuePredefined) return null;
            var own = setting.SettingLocation == DRSSettingLocation.CurrentProfile
                || (isBase && setting.SettingLocation == DRSSettingLocation.BaseProfile);
            if (!own) return null;
            var raw = setting.CurrentValue;
            return raw is uint u ? u : Convert.ToUInt32(raw);
        }
        catch (NVIDIAApiException ex) when (ex.Status == Status.SettingNotFound)
        {
            return null;
        }
    }

    /// <summary>Applies a restore plan to one profile. False when any setting could not be written.</summary>
    private static bool RestoreProfile(DriverSettingsProfile profile, IReadOnlyList<DrsRestoreAction> plan)
    {
        var ok = true;
        foreach (var action in plan)
        {
            try
            {
                if (action.Value is uint v)
                    profile.SetSetting(action.SettingId, DRSSettingType.Integer, v);
                else
                    profile.RestoreSettingToDefault(action.SettingId);
            }
            catch (NVIDIAApiException ex) when (ex.Status == Status.SettingNotFound)
            {
                // Already at NVIDIA's default: nothing set on this profile.
            }
            catch (NVIDIAApiException ex)
            {
                ok = false;
                Debug.WriteLine($"RestoreProfile: 0x{action.SettingId:X8} failed: {ex.Status}");
            }
        }
        return ok;
    }

    /// <summary>Game-profile names from the valid index, or a full scan when there is none.</summary>
    private static List<string> GameProfileNames(DriverSettingsSession session, CancellationToken ct)
    {
        var index = ProfileIndexStore.LoadValid(GetDriverVersionString());
        if (index != null) return index.GameProfileNames.ToList();

        var names = new List<string>();
        foreach (var profile in session.Profiles)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (profile is null || !profile.IsValid || profile.NumberOfApplications <= 0) continue;
                names.Add(profile.Name);
            }
            catch (NVIDIAApiException pex)
            {
                Debug.WriteLine($"GameProfileNames: skipped a profile: {pex.Status}");
            }
        }
        return names;
    }

    /// <summary>Driver version string used to invalidate the profile index. Never throws.</summary>
    private static string GetDriverVersionString()
    {
        try
        {
            return NVIDIA.DriverVersion.ToString();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"GetDriverVersionString failed: {ex.Message}");
            return "unknown";
        }
    }

    /// <summary>
    /// Applies the per-feature override enables + render presets to a single DRS profile.
    /// <paramref name="srPresetValue"/> is the DLSS-SR preset; RR and FG take their OWN presets
    /// from <paramref name="options"/> (each feature has an independent preset-selection ID — do
    /// NOT cross-assign). Each feature's override is ON exactly when its own preset is not
    /// Default (v0.77); a Default feature has its override turned OFF.
    /// </summary>
    private static void ApplyToProfile(DriverSettingsProfile profile, uint srPresetValue, PresetApplyOptions options)
    {
        static uint OnOff(bool on) => on ? DlssPresetSettingIds.OVERRIDE_ON : DlssPresetSettingIds.OVERRIDE_OFF;
        var srOn = srPresetValue != (uint)DlssPreset.Default;
        var rrOn = options.RayReconstructionPreset != DlssPreset.Default;
        var fgOn = options.FrameGenerationPreset != DlssPreset.Default;

        if (options.EnableSuperResolution)
        {
            // Enable flag MUST be set or the preset selection is ignored ("Custom" vs default).
            profile.SetSetting(DlssPresetSettingIds.SR_OVERRIDE_ENABLE, DRSSettingType.Integer, OnOff(srOn));
            if (srOn)
                profile.SetSetting(DlssPresetSettingIds.SR_RENDER_PRESET, DRSSettingType.Integer, srPresetValue);
        }

        if (options.EnableRayReconstruction)
        {
            // DLSS-RR ("NR" / Ray Reconstruction denoiser) override + its OWN preset selection.
            // BUG FIX (v0.0.35): previously the SR letter was mirrored onto RR.
            profile.SetSetting(DlssPresetSettingIds.RR_OVERRIDE_ENABLE, DRSSettingType.Integer, OnOff(rrOn));
            if (rrOn)
                profile.SetSetting(DlssPresetSettingIds.RR_RENDER_PRESET, DRSSettingType.Integer, (uint)options.RayReconstructionPreset);
        }

        if (options.EnableFrameGeneration)
        {
            // DLSS-FG (Frame Generation) override + its OWN preset selection (0x10E41DF1).
            profile.SetSetting(DlssPresetSettingIds.FG_OVERRIDE_ENABLE, DRSSettingType.Integer, OnOff(fgOn));
            if (fgOn)
            {
                profile.SetSetting(DlssPresetSettingIds.FG_RENDER_PRESET, DRSSettingType.Integer, (uint)options.FrameGenerationPreset);

                // DLSSG generator MODE + MULTIPLIER (the Fixed/Dynamic + 2x/3x/4x… knobs).
                // SEPARATE setting family from the enable flag + render preset above. Without
                // these, enabling the FG override does NOT select Fixed vs Dynamic or the frame
                // multiplier — which is why in-game toggles and the toolkit couldn't change them
                // and the NVIDIA App was the only way (it writes these IDs). Only written when the
                // caller explicitly picks a mode (Disabled = leave the driver/app value alone, the
                // back-compat default).
                ApplyDlssgMode(profile, options);
            }
        }
    }

    /// <summary>
    /// Writes the DLSSG mode + multiplier (+ dynamic target FPS) for a profile. No-op when the
    /// mode is Disabled so existing callers that don't set a mode leave the driver value untouched.
    /// </summary>
    private static void ApplyDlssgMode(DriverSettingsProfile profile, PresetApplyOptions options)
    {
        var mode = options.FrameGenerationMode;
        if (mode == DlssgMode.Disabled)
            return; // caller didn't ask to change the mode — leave it as-is

        profile.SetSetting(DlssPresetSettingIds.DLSSG_MODE, DRSSettingType.Integer, (uint)mode);

        switch (mode)
        {
            case DlssgMode.On:
                // Fixed multiplier: write the generated-frame COUNT (multiplier - 1).
                profile.SetSetting(
                    DlssPresetSettingIds.DLSSG_MULTI_FRAME_COUNT,
                    DRSSettingType.Integer,
                    DlssPresetDisplay.MultiplierToFrameCount(options.FrameGenerationMultiplier));
                break;

            case DlssgMode.Dynamic:
                // Dynamic: cap the generated-frame count (multiplier - 1) and set the target FPS.
                profile.SetSetting(
                    DlssPresetSettingIds.DLSSG_DYNAMIC_MULTI_FRAME_COUNT_MAX,
                    DRSSettingType.Integer,
                    DlssPresetDisplay.MultiplierToFrameCount(options.FrameGenerationMultiplier));

                // null target = AUTO ("match max refresh rate"); a positive value = explicit FPS.
                var targetFps = options.FrameGenerationDynamicTargetFps is int fps && fps > 0
                    ? (uint)fps
                    : DlssPresetSettingIds.DLSSG_DYNAMIC_TARGET_FRAME_RATE_AUTO;
                profile.SetSetting(
                    DlssPresetSettingIds.DLSSG_DYNAMIC_TARGET_FRAME_RATE,
                    DRSSettingType.Integer,
                    targetFps);
                break;

            // Off / Auto: the mode value alone is sufficient; no multiplier/target to write.
        }
    }

    private static bool _initialized;
    private static readonly Lock _initLock = new();

    private static void EnsureInitialized()
    {
        lock (_initLock)
        {
            if (!_initialized)
            {
                NVIDIA.Initialize();
                _initialized = true;
            }
        }
    }

    private static bool CheckAvailability()
    {
        try
        {
            EnsureInitialized();
            return true;
        }
        catch (DllNotFoundException)
        {
            Debug.WriteLine("PresetOverrideService: nvapi64.dll not found — NVIDIA DRS unavailable");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"PresetOverrideService: NVIDIA init failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Raw integer value of a DRS setting on a profile, or null when it is not set.</summary>
    private static uint? ReadUInt(DriverSettingsProfile profile, uint settingId)
    {
        try
        {
            var setting = profile.GetSetting(settingId);
            if (setting is null) return null;
            var raw = setting.CurrentValue;
            return raw is uint u ? u : Convert.ToUInt32(raw);
        }
        catch (NVIDIAApiException ex) when (ex.Status == Status.SettingNotFound)
        {
            // NvAPIWrapper reports an unset setting by throwing, not by returning null.
            return null;
        }
    }

    /// <summary>
    /// Maps a raw DRS setting value to a <see cref="DlssPreset"/> enum. Because the enum's
    /// underlying values ARE the DRS preset values (A=1…M=13, Default=0, Latest=0x00FFFFFF),
    /// this is a checked cast — any value not defined in the enum maps to Default.
    /// </summary>
    public static DlssPreset PresetFromValue(uint value) =>
        Enum.IsDefined(typeof(DlssPreset), value) ? (DlssPreset)value : DlssPreset.Default;
}
