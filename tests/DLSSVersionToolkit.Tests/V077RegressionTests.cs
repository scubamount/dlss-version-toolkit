using DLSSVersionToolkit.Core.Models;
using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.77 regression gates (holistic audit 2026-10-05). One test per root cause; the source-shape
/// tests cover UI wiring that has no unit seam (WPF view model, not referenced by this project).
/// </summary>
public class V077RegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dvt-v077-{Guid.NewGuid():N}");

    public V077RegressionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static string SrcFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { SiblingSweepTests.FindRepoSubdir("src") }.Concat(parts).ToArray()));

    private static string Vm() => SrcFile("DLSSVersionToolkit", "ViewModels", "MainViewModel.cs");

    // ---- DRS baseline (Reset restores, not "override off") ------------------------------

    private static readonly uint Sr = DlssPresetSettingIds.SR_OVERRIDE_ENABLE;
    private static readonly uint SrPreset = DlssPresetSettingIds.SR_RENDER_PRESET;

    [Fact]
    public void DrsPlan_Trusted_RestoresCapturedUserValue_AndDefaultsTheRest()
    {
        var b = new DrsBaseline { Trusted = true };
        b.Profiles["Cyberpunk 2077"] = new() { [Sr] = 1, [SrPreset] = 11 };   // user's own K override

        var plan = DrsBaselineStore.PlanRestore(b, "Cyberpunk 2077");

        Assert.Equal(DrsBaselineStore.ManagedSettingIds.Count, plan.Count);
        Assert.Contains(new DrsRestoreAction(Sr, 1), plan);
        Assert.Contains(new DrsRestoreAction(SrPreset, 11), plan);
        // Everything the user never set goes back to NVIDIA's default — never to 0.
        Assert.All(plan.Where(a => a.SettingId != Sr && a.SettingId != SrPreset), a => Assert.Null(a.Value));
    }

    [Fact]
    public void DrsPlan_Trusted_UncapturedProfile_IsLeftAlone()
    {
        var b = new DrsBaseline { Trusted = true };
        Assert.Empty(DrsBaselineStore.PlanRestore(b, "Never Touched"));
    }

    [Fact]
    public void DrsPlan_Untrusted_ReturnsEverythingToNvidiaDefault()
    {
        // An upgrade from v0.76: captures may hold an older build's writes, so none are trusted.
        var b = new DrsBaseline { Trusted = false };
        b.Profiles["Game"] = new() { [Sr] = 1, [SrPreset] = 12 };

        var plan = DrsBaselineStore.PlanRestore(b, "Game");

        Assert.Equal(DrsBaselineStore.ManagedSettingIds.Count, plan.Count);
        Assert.All(plan, a => Assert.Null(a.Value));
    }

    [Fact]
    public void DrsStore_CapturesOnce_AndPersists()
    {
        var store = new DrsBaselineStore(_root);
        Assert.True(store.EnsureCreated(trusted: true));
        Assert.False(store.EnsureCreated(trusted: false));     // idempotent; trust never flips

        store.RecordIfAbsent("Game", new Dictionary<uint, uint?> { [Sr] = null });
        store.RecordIfAbsent("Game", new Dictionary<uint, uint?> { [Sr] = 1 });   // toolkit's own write
        store.Flush();

        var reloaded = new DrsBaselineStore(_root).Load();
        Assert.True(reloaded.Trusted);
        Assert.Null(reloaded.Profiles["Game"][Sr]);
    }

    [Fact]
    public void DrsStore_MissingFile_IsUntrusted()
    {
        Assert.False(new DrsBaselineStore(_root).Load().Trusted);
    }

    [Fact]
    public void DrsStore_FreshInstall_OnlyWhenNoEarlierBuildLeftFiles()
    {
        var store = new DrsBaselineStore(_root);
        Assert.True(store.LooksLikeFreshInstall());

        File.WriteAllText(Path.Combine(_root, "profile-index.json"), "{}");
        Assert.False(store.LooksLikeFreshInstall());            // an earlier apply ran here
    }

    [Theory]
    [InlineData("settings.json")]          // v0.0.38 users who never built a profile index
    [InlineData("overrides.json")]
    [InlineData("override-baseline.json")]
    public void DrsStore_AnyEarlierLaunchFile_MeansUntrusted(string marker)
    {
        var store = new DrsBaselineStore(_root);
        File.WriteAllText(Path.Combine(_root, marker), "{}");
        Assert.False(store.LooksLikeFreshInstall());
    }

    [Fact]
    public void DrsStore_CleanReset_ClearsSpentCaptures_AndTrusts()
    {
        var store = new DrsBaselineStore(_root);
        store.EnsureCreated(trusted: false);
        store.RecordIfAbsent("Game", new Dictionary<uint, uint?> { [Sr] = 1 });
        store.MarkCleanAndTrusted();

        var reloaded = new DrsBaselineStore(_root).Load();
        Assert.True(reloaded.Trusted);
        Assert.Empty(reloaded.Profiles);   // a later apply captures the post-reset state afresh
    }

    [Fact]
    public void Apply_PersistsCapturesBeforeDriverSave()
    {
        var src = SrcFile("DLSSVersionToolkit.Core", "Services", "PresetOverrideService.cs");
        var flush = src.IndexOf("_baseline.Flush();", StringComparison.Ordinal);
        Assert.True(flush > 0);
        Assert.True(src.IndexOf("session.Save();", flush, StringComparison.Ordinal) > flush,
            "captures must be persisted before the driver save");
        Assert.Contains("if (profilesSkipped == 0)\n                    _baseline.MarkCleanAndTrusted();",
            src.Replace("\r\n", "\n"));
    }

    [Fact]
    public void UpdateAll_LeavesDefaultFeaturesUnwritten()
    {
        var vm = Vm();
        Assert.Contains("EnableSuperResolution = SelectedPreset is { } sr && sr != DlssPreset.Default", vm);
        Assert.Contains("EnableRayReconstruction = SelectedRrPreset != DlssPreset.Default", vm);
        Assert.Contains("EnableFrameGeneration = SelectedFgPreset != DlssPreset.Default", vm);
    }

    [Fact]
    public void ResetDialog_WarnsWhenItWillDropNvidiaAppPresets()
    {
        var vm = Vm();
        var at = vm.IndexOf("private async Task ResetOverridesAsync()", StringComparison.Ordinal);
        var body = vm[at..vm.IndexOf("[RelayCommand]", at + 10, StringComparison.Ordinal)];
        Assert.Contains("_presetOverrideService.DrsBaselineTrusted", body);
        Assert.Contains("per-game presets you set in the NVIDIA App", body);
    }

    [Fact]
    public void AnWave_DeletesCachedArchiveOnlyOnArchiveFailure()
    {
        var src = SrcFile("DLSSVersionToolkit.Core", "Services", "AnWaveAutoService.cs");
        var del = src.IndexOf("TryDeleteFile(glomPath);", StringComparison.Ordinal);
        var guard = src.LastIndexOf("catch (Exception ex)", del, StringComparison.Ordinal);
        Assert.Contains("when (ex is SharpCompressException", src[guard..del]);
    }

    [Fact]
    public void DrsStore_ManagedIds_CoverEveryIdTheApplierWrites()
    {
        var src = SrcFile("DLSSVersionToolkit.Core", "Services", "PresetOverrideService.cs");
        var ids = System.Text.RegularExpressions.Regex.Matches(src, @"SetSetting\(\s*DlssPresetSettingIds\.(\w+)")
            .Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.NotEmpty(ids);
        foreach (var name in ids)
        {
            var value = (uint)typeof(DlssPresetSettingIds).GetField(name)!.GetValue(null)!;
            Assert.Contains(value, DrsBaselineStore.ManagedSettingIds);
        }
    }

    [Fact]
    public void Reset_RestoresDrsBaseline_InsteadOfWritingDefaultPreset()
    {
        var vm = Vm();
        var at = vm.IndexOf("private async Task ResetOverridesAsync()", StringComparison.Ordinal);
        var body = vm[at..vm.IndexOf("[RelayCommand]", at + 10, StringComparison.Ordinal)];
        Assert.Contains("RestoreBaselineAsync(", body);
        Assert.DoesNotContain("ApplyPresetAsync(DlssPreset.Default", body);
    }

    // ---- Per-feature presets ------------------------------------------------------------

    [Fact]
    public void UpdateAll_AppliesPresets_WhenOnlyRrOrFgIsSet()
    {
        var vm = Vm();
        Assert.Contains("SelectedRrPreset != DlssPreset.Default", vm);
        Assert.Contains("SelectedFgPreset != DlssPreset.Default", vm);
    }

    [Fact]
    public void Applier_GatesEachFeatureOnItsOwnPreset()
    {
        var src = SrcFile("DLSSVersionToolkit.Core", "Services", "PresetOverrideService.cs");
        Assert.Contains("var rrOn = options.RayReconstructionPreset != DlssPreset.Default;", src);
        Assert.Contains("var fgOn = options.FrameGenerationPreset != DlssPreset.Default;", src);
        Assert.DoesNotContain("bool enable, PresetApplyOptions options", src);
    }

    // ---- Reset: indicator capture failure ------------------------------------------------

    [Fact]
    public void Baseline_IndicatorReadFailure_IsRecorded_NotTreatedAsAbsent()
    {
        var manifest = new OverrideManifestService(new VersionComparer(), _root);
        var svc = new OverrideResetService(manifest, _root, Path.Combine(_root, "nvngx_config.txt"));

        Assert.True(svc.EnsureBaselineCaptured(indicatorRawValue: null, indicatorCaptureFailed: true));

        var b = svc.LoadBaseline()!;
        Assert.True(b.IndicatorCaptureFailed);
        Assert.Null(b.IndicatorRawValue);
        // And the Reset path checks the flag before deleting anything.
        Assert.Contains("IndicatorCaptureFailed: true", Vm());
    }

    // ---- Truthful text ------------------------------------------------------------------

    [Fact]
    public void LastRunPanel_RaisesHasRunSteps()
    {
        Assert.Contains("OnPropertyChanged(nameof(HasRunSteps))", Vm());
    }

    [Fact]
    public void Dialogs_NameOnlyButtonsThatExist()
    {
        var vm = Vm();
        Assert.DoesNotContain("'Sync from DLSS SDK'", vm);
        Assert.DoesNotContain("Advanced menu", vm);
        Assert.DoesNotContain("'Apply to all games'", vm);
        Assert.DoesNotContain("\"Apply to all games and", vm);
    }

    [Fact]
    public void UnlockRunReport_FailureIsWarn_SuccessIsOk()
    {
        Assert.Contains("_runReports.Add(\"Unlock\", unlockResult.Success ? \"ok\" : \"warn\"", Vm());
    }

    [Fact]
    public void ServiceRestart_FailedStartIsAnError()
    {
        var src = SrcFile("DLSSVersionToolkit.Core", "Services", "WhitelistService.cs");
        Assert.DoesNotContain("may have failed", src);
        Assert.Contains("did not start again", src);
    }

    [Fact]
    public void WhitelistDetector_UnreadableFile_IsNotApplied()
    {
        var src = SrcFile("DLSSVersionToolkit.Core", "Services", "WhitelistService.cs");
        Assert.Contains("anyReadFailed ? WhitelistState.Unreadable : WhitelistState.Applied", src);
        Assert.Contains("WhitelistState.Unreadable =>", Vm());
    }

    [Fact]
    public void AnWaveDownload_WritesPartFile_AndChecksLength()
    {
        var src = SrcFile("DLSSVersionToolkit.Core", "Services", "AnWaveAutoService.cs");
        Assert.Contains("destPath + \".part\"", src);
        Assert.Contains("totalRead != expected", src);
        Assert.Contains("File.Move(partPath, destPath, overwrite: true)", src);
    }

    [Fact]
    public void TimerScan_SkipsWhileMutating()
    {
        var app = SrcFile("DLSSVersionToolkit", "App.xaml.cs");
        Assert.Contains("IsBusyMutating: false", app);
        Assert.Contains("public bool IsBusyMutating", Vm());
    }

    [Fact]
    public void RrDefaultText_MatchesCodeDefault()
    {
        Assert.Equal(DlssPreset.F, DlssPresetDisplay.RayReconstructionDefault);
        var xaml = SrcFile("DLSSVersionToolkit", "MainWindow.xaml");
        Assert.DoesNotContain("RR E,", xaml);
        Assert.Contains("RR F,", xaml);
    }
}
