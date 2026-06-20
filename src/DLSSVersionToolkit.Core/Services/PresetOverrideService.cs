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
    int GameProfilesUpdated = 0);

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
    /// defaults to the recommended RR preset (E). Set to Default to clear the RR preset selection.
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
    /// overrides. Requires admin privileges.
    /// </summary>
    Task<PresetOverrideResult> ApplyPresetAsync(DlssPreset preset, PresetApplyOptions? options = null, CancellationToken ct = default);

    /// <summary>
    /// Checks whether the NVIDIA DRS API is available (i.e., NVIDIA drivers are installed).
    /// </summary>
    bool IsAvailable { get; }
}

[SupportedOSPlatform("windows")]
public sealed class PresetOverrideService : IPresetOverrideService
{
    private bool? _isAvailable;

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
                var profile = session.CurrentGlobalProfile;
                if (profile is null)
                {
                    return new PresetOverrideResult(false, null, "Could not get global profile.");
                }

                var setting = profile.GetSetting(DlssPresetSettingIds.SR_RENDER_PRESET);
                if (setting is null)
                {
                    // No override set — driver default
                    return new PresetOverrideResult(true, DlssPreset.Default, null);
                }

                var rawValue = setting.CurrentValue;
                var presetValue = rawValue is uint u ? u : (uint?)Convert.ToUInt32(rawValue);
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

    public async Task<PresetOverrideResult> ApplyPresetAsync(DlssPreset preset, PresetApplyOptions? options = null, CancellationToken ct = default)
    {
        options ??= new PresetApplyOptions();
        return await Task.Run(() =>
        {
            try
            {
                EnsureInitialized();
                using var session = DriverSettingsSession.CreateAndLoad();

                var presetValue = (uint)preset;
                bool enable = preset != DlssPreset.Default;
                int profilesUpdated = 0;
                int gameProfilesUpdated = 0;

                // 1) Base profile = the global default inherited by profiles that don't
                //    override these settings themselves.
                var baseProfile = session.BaseProfile;
                if (baseProfile is not null)
                {
                    ApplyToProfile(baseProfile, presetValue, enable, options);
                    profilesUpdated++;
                }

                // 2) Every game profile that actually exists on THIS system. This is the key
                //    fix: games with their own DRS profile do NOT inherit the base setting, so
                //    the preset only takes effect in-game when we set it on each game's profile.
                //
                //    PERF (root-cause): NvAPIWrapper's DriverSettingsProfile re-fetches the full
                //    NVDRS_PROFILE struct via NvAPI_DRS_GetProfileInfo on EVERY property access
                //    (NumberOfApplications, IsPredefined, Name all call GetProfileInfo with no
                //    caching). NVIDIA ships ~8000 predefined profiles, so reading a property per
                //    profile = ~8000 P/Invokes + struct marshals, most for games the user doesn't
                //    own. We minimise this two ways:
                //      (a) materialise the profile list once (EnumProfiles is a single call);
                //      (b) read NumberOfApplications EXACTLY ONCE per profile into a local — never
                //          touch a second GetProfileInfo-backed property in the loop.
                //    A profile with 0 applications affects no game on this machine, so writing to
                //    it is pointless (and for NVIDIA's predefined DB entries, actively wrong — it
                //    would override NVIDIA's per-game tuning for games not installed here).
                if (options.ApplyToAllGameProfiles)
                {
                    foreach (var profile in session.Profiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            if (profile is null || !profile.IsValid)
                                continue;

                            // Single GetProfileInfo-backed read per profile (cached in a local).
                            int appCount = profile.NumberOfApplications;
                            if (appCount <= 0)
                                continue;

                            ApplyToProfile(profile, presetValue, enable, options);
                            profilesUpdated++;
                            gameProfilesUpdated++;
                        }
                        catch (NVIDIAApiException pex)
                        {
                            // Don't let one stubborn profile abort the whole sweep.
                            Debug.WriteLine($"PresetOverrideService: skipped a profile: {pex.Status}");
                        }
                    }
                }

                session.Save();

                Debug.WriteLine($"PresetOverrideService: Applied preset {preset} (0x{presetValue:X}) enable={enable} to {profilesUpdated} profile(s) ({gameProfilesUpdated} game).");
                return new PresetOverrideResult(true, preset, null, false, profilesUpdated, gameProfilesUpdated);
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

    /// <summary>
    /// Applies the per-feature override enables + render presets to a single DRS profile.
    /// <paramref name="srPresetValue"/> is the DLSS-SR preset; RR and FG take their OWN presets
    /// from <paramref name="options"/> (each feature has an independent preset-selection ID — do
    /// NOT cross-assign). When <paramref name="enable"/> is false (SR preset = Default), every
    /// override is turned OFF so behavior reverts to the driver/app default.
    /// </summary>
    private static void ApplyToProfile(DriverSettingsProfile profile, uint srPresetValue, bool enable, PresetApplyOptions options)
    {
        var onOff = enable ? DlssPresetSettingIds.OVERRIDE_ON : DlssPresetSettingIds.OVERRIDE_OFF;

        if (options.EnableSuperResolution)
        {
            // Enable flag MUST be set or the preset selection is ignored ("Custom" vs default).
            profile.SetSetting(DlssPresetSettingIds.SR_OVERRIDE_ENABLE, DRSSettingType.Integer, onOff);
            profile.SetSetting(DlssPresetSettingIds.SR_RENDER_PRESET, DRSSettingType.Integer, srPresetValue);
        }

        if (options.EnableRayReconstruction)
        {
            // DLSS-RR ("NR" / Ray Reconstruction denoiser) override + its OWN preset selection.
            // BUG FIX (v0.0.35): previously the SR letter was mirrored onto RR, so RR got L when
            // it should default to E. RR now uses options.RayReconstructionPreset.
            profile.SetSetting(DlssPresetSettingIds.RR_OVERRIDE_ENABLE, DRSSettingType.Integer, onOff);
            if (enable)
                profile.SetSetting(DlssPresetSettingIds.RR_RENDER_PRESET, DRSSettingType.Integer, (uint)options.RayReconstructionPreset);
        }

        if (options.EnableFrameGeneration)
        {
            // DLSS-FG (Frame Generation) override + its OWN preset selection (0x10E41DF1).
            // NEW (v0.0.35): FG previously had no preset selection set at all.
            profile.SetSetting(DlssPresetSettingIds.FG_OVERRIDE_ENABLE, DRSSettingType.Integer, onOff);
            if (enable)
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

    /// <summary>
    /// Maps a raw DRS setting value to a <see cref="DlssPreset"/> enum. Because the enum's
    /// underlying values ARE the DRS preset values (A=1…M=13, Default=0, Latest=0x00FFFFFF),
    /// this is a checked cast — any value not defined in the enum maps to Default.
    /// </summary>
    public static DlssPreset PresetFromValue(uint value) =>
        Enum.IsDefined(typeof(DlssPreset), value) ? (DlssPreset)value : DlssPreset.Default;
}
