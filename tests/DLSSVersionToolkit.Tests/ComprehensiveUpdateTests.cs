using System.IO;
using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Tests for the comprehensive Update-All fix (v0.0.36): version is read from the DLL's
/// FileVersionInfo (not a nonexistent package config), the DLSS demo layout
/// (DLSS_Sample_App/bin/ngx_dlss_demo/) is recognized, and the Streamline SDK is the
/// comprehensive 4-DLL source. These tests cover the platform-agnostic pieces; the actual
/// FileVersionInfo read is exercised by CI on windows-latest against real DLLs at runtime.
/// </summary>
public class ComprehensiveUpdateTests
{
    // --- DllVersionReader: graceful handling of missing / no-version files ---

    [Fact]
    public void ReadFileVersion_MissingFile_ReturnsNull()
    {
        Assert.Null(DllVersionReader.ReadFileVersion(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz.dll")));
    }

    [Fact]
    public void ReadFileVersion_NullOrEmptyPath_ReturnsNull()
    {
        Assert.Null(DllVersionReader.ReadFileVersion(""));
        Assert.Null(DllVersionReader.ReadFileVersion(null!));
    }

    [Fact]
    public void ReadDlssVersionFromFolder_MissingFolder_ReturnsNull()
    {
        Assert.Null(DllVersionReader.ReadDlssVersionFromFolder(Path.Combine(Path.GetTempPath(), "no-such-folder-xyz")));
    }

    [Fact]
    public void ReadDlssVersionFromFolder_FolderWithoutDll_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dlsstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // A folder with some other file but no nvngx_dlss.dll
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "x");
            Assert.Null(DllVersionReader.ReadDlssVersionFromFolder(dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ReadFileVersion_NonPeFile_ReturnsNullGracefully()
    {
        // A text file named like a DLL must not throw — FileVersionInfo returns empty fields.
        var dir = Path.Combine(Path.GetTempPath(), $"dlsstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var fake = Path.Combine(dir, "nvngx_dlss.dll");
            File.WriteAllText(fake, "not a real PE");
            // Should not throw; returns null (no version resource) on a non-PE file.
            var result = DllVersionReader.ReadFileVersion(fake);
            Assert.Null(result);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ReadComponentVersion_MissingDll_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dlsstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(DllVersionReader.ReadComponentVersion(dir, "nvngx_dlssg.dll"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ReadComponentVersion_MissingFolder_ReturnsNull()
    {
        Assert.Null(DllVersionReader.ReadComponentVersion(
            Path.Combine(Path.GetTempPath(), "no-such-xyz"), "nvngx_dlss.dll"));
    }

    [Fact]
    public void NgxConfigParser_NoConfigNoDll_ReportsUnknown()
    {
        // The fix: a version folder with no config and no DLL must still parse cleanly to Unknown
        // (not crash), and report "Config file not found".
        var dir = Path.Combine(Path.GetTempPath(), $"dlsstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var result = new NgxConfigParser().Parse(dir);
            Assert.Equal("Unknown", result.DLSS);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void NgxConfigParser_StaleConfigKept_WhenNoDllPresent()
    {
        // With a config but no DLL, the parsed config version is used (DLL override is a no-op
        // when the DLL is absent). Confirms the override only fires when a real DLL exists.
        var dir = Path.Combine(Path.GetTempPath(), $"dlsstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "nvngx_package_config.txt"), "dlss, 310.6.0.0\n");
            var result = new NgxConfigParser().Parse(dir);
            Assert.Equal("310.6.0.0", result.DLSS);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

/// <summary>
/// Tests for the v0.0.38 fixes: preset-selection persistence (fixes "resets to Preset L on
/// relaunch"), the FG multiplier default change (4x → 6x, matching the NVIDIA App default),
/// and the shared NGX path resolver (explicit → registry → defaults).
/// </summary>
public class PresetPersistenceAndPathsTests
{
    // --- FG multiplier default (issue #2) ---

    [Fact]
    public void FrameGenMultiplierDefault_Is6x()
    {
        // v0.0.38: default aligned with the NVIDIA App's "Dynamic, up to 6x" (was 4x).
        Assert.Equal(6, DLSSVersionToolkit.Core.Models.DlssPresetDisplay.FrameGenMultiplierDefault);
        // And 6x must remain a selectable option so the default is always in the dropdown.
        Assert.Contains(6, DLSSVersionToolkit.Core.Models.DlssPresetDisplay.FrameGenMultipliers);
    }

    // --- PresetSelectionPersistence: round-trip (issue #3) ---

    [Fact]
    public void PresetSelection_RoundTrips_ThroughAppSettings()
    {
        var settings = new DLSSVersionToolkit.Core.Models.AppSettings();
        PresetSelectionPersistence.ApplyTo(settings,
            srPreset: DLSSVersionToolkit.Core.Models.DlssPreset.K,
            rrPreset: DLSSVersionToolkit.Core.Models.DlssPreset.E,
            fgPreset: DLSSVersionToolkit.Core.Models.DlssPreset.B,
            fgMode: DLSSVersionToolkit.Core.Models.DlssgMode.Dynamic,
            fgMultiplier: 6);

        Assert.Equal(DLSSVersionToolkit.Core.Models.DlssPreset.K, PresetSelectionPersistence.ParsePreset(settings.SelectedSrPreset));
        Assert.Equal(DLSSVersionToolkit.Core.Models.DlssPreset.E, PresetSelectionPersistence.ParsePreset(settings.SelectedRrPreset));
        Assert.Equal(DLSSVersionToolkit.Core.Models.DlssPreset.B, PresetSelectionPersistence.ParsePreset(settings.SelectedFgPreset));
        Assert.Equal(DLSSVersionToolkit.Core.Models.DlssgMode.Dynamic, PresetSelectionPersistence.ParseMode(settings.SelectedFgMode));
        Assert.Equal(6, PresetSelectionPersistence.ParseMultiplier(settings.SelectedFgMultiplier));
    }

    [Fact]
    public void PresetSelection_EmptyOrUnknown_ReturnsNull_SoDefaultsAreKept()
    {
        // Empty = never saved (fresh install / pre-v0.0.38 settings.json) → keep defaults.
        Assert.Null(PresetSelectionPersistence.ParsePreset(""));
        Assert.Null(PresetSelectionPersistence.ParsePreset(null));
        Assert.Null(PresetSelectionPersistence.ParsePreset("ZZ_not_a_preset"));
        // Numeric garbage must not map to an undefined enum value.
        Assert.Null(PresetSelectionPersistence.ParsePreset("999"));

        Assert.Null(PresetSelectionPersistence.ParseMode(""));
        Assert.Null(PresetSelectionPersistence.ParseMode("Sideways"));

        // 0 = "not saved" sentinel; out-of-range values rejected.
        Assert.Null(PresetSelectionPersistence.ParseMultiplier(0));
        Assert.Null(PresetSelectionPersistence.ParseMultiplier(1));
        Assert.Null(PresetSelectionPersistence.ParseMultiplier(99));
    }

    [Fact]
    public void PresetSelection_ParseIsCaseInsensitive()
    {
        Assert.Equal(DLSSVersionToolkit.Core.Models.DlssPreset.K, PresetSelectionPersistence.ParsePreset("k"));
        Assert.Equal(DLSSVersionToolkit.Core.Models.DlssPreset.Latest, PresetSelectionPersistence.ParsePreset("latest"));
        Assert.Equal(DLSSVersionToolkit.Core.Models.DlssgMode.Dynamic, PresetSelectionPersistence.ParseMode("DYNAMIC"));
    }

    [Fact]
    public async Task PresetSelection_SurvivesSettingsJsonRoundTrip()
    {
        // Full JSON serialize → deserialize round-trip (same path SettingsService uses),
        // proving pre-v0.0.38 settings.json (missing the new fields) also loads cleanly.
        var settings = new DLSSVersionToolkit.Core.Models.AppSettings();
        PresetSelectionPersistence.ApplyTo(settings,
            DLSSVersionToolkit.Core.Models.DlssPreset.K,
            DLSSVersionToolkit.Core.Models.DlssPreset.E,
            DLSSVersionToolkit.Core.Models.DlssPreset.B,
            DLSSVersionToolkit.Core.Models.DlssgMode.Dynamic, 6);

        var json = System.Text.Json.JsonSerializer.Serialize(settings);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<DLSSVersionToolkit.Core.Models.AppSettings>(json)!;
        Assert.Equal("K", loaded.SelectedSrPreset);
        Assert.Equal(6, loaded.SelectedFgMultiplier);

        // Old settings.json without the new fields → defaults (empty/0) → parse to null.
        var legacy = System.Text.Json.JsonSerializer.Deserialize<DLSSVersionToolkit.Core.Models.AppSettings>("{\"NgxBasePath\":\"\"}")!;
        Assert.Null(PresetSelectionPersistence.ParsePreset(legacy.SelectedSrPreset));
        Assert.Null(PresetSelectionPersistence.ParseMultiplier(legacy.SelectedFgMultiplier));
        await Task.CompletedTask;
    }

    // --- NgxPathResolver (issue #4a) ---

    [Fact]
    public void NgxPathResolver_ExplicitPathIsFirst_AndDeduplicated()
    {
        var explicitPath = Path.Combine(Path.GetTempPath(), "custom-ngx");
        var candidates = NgxPathResolver.GetCandidatePaths(explicitPath);

        Assert.Equal(explicitPath, candidates[0]);
        // Passing the same explicit path twice through defaults must not duplicate.
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void NgxPathResolver_NoExplicitPath_StillReturnsDefaults()
    {
        var candidates = NgxPathResolver.GetCandidatePaths(null);
        // On any OS the two SpecialFolder-derived defaults must be present (non-empty list).
        Assert.NotEmpty(candidates);
        Assert.All(candidates, c => Assert.False(string.IsNullOrWhiteSpace(c)));
    }

    [Fact]
    public void NgxPathResolver_RegistryProbe_NeverThrows()
    {
        // On non-Windows (and on Windows without an NVIDIA driver) this must return an empty
        // list rather than throwing — the resolver is called on every scan.
        var ex = Record.Exception(() => NgxPathResolver.GetRegistryNgxPaths());
        Assert.Null(ex);
    }
}
