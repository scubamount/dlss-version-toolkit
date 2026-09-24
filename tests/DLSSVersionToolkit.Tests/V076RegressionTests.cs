using System.IO.Compression;
using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.76 regression gates, one per root cause behind the ed592e/3e318a screenshots:
///  - Streamline: the aarch64 SDK asset was picked over x64 (identical names + versions).
///  - Scan order: the NGX Release row came from a root Update All never writes.
///  - Override state: the success dialog asserted "globally active" without reading the config.
///  - Reset: must restore the pre-toolkit config, never capture the toolkit's own block as original.
///  - Stale results: ScanAsync dropped (instead of queued) a refresh requested mid-scan.
/// </summary>
public class V076RegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dvt-v076-{Guid.NewGuid():N}");

    public V076RegressionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static string SrcFile(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { SiblingSweepTests.FindRepoSubdir("src") }.Concat(parts).ToArray()));

    // ---- Streamline architecture -------------------------------------------------------

    [Theory]
    [InlineData("streamline-sdk-v2.14.1.zip", true)]
    [InlineData("streamline-sdk-2.12.0.zip", true)]
    [InlineData("streamline-sdk-v2.15.0.zip", true)]   // a future version still matches
    [InlineData("streamline-sdk-v2.14.1-aarch64.zip", false)]
    [InlineData("streamline-sdk-v2.14.1-arm64.zip", false)]
    [InlineData("streamline-sdk-v2.14.1-debug.zip", false)]
    [InlineData("streamline-sdk-v2.14.1.tar.gz", false)]
    [InlineData("streamline-sdk-v2.zip", false)]            // not a version
    [InlineData("streamline-sdk-v2.14.1.0.0.zip", false)]   // >4 parts
    public void StreamlineAssetFilter_OnlyPlainX64Zip(string name, bool expected) =>
        Assert.Equal(expected, StreamlineDownloadService.IsX64SdkAssetName(name));

    private static byte[] Pe(ushort machine)
    {
        var data = new byte[1024];
        data[0] = (byte)'M'; data[1] = (byte)'Z';
        data[0x3C] = 0x80;
        data[0x80] = (byte)'P'; data[0x81] = (byte)'E';
        data[0x84] = (byte)(machine & 0xFF);
        data[0x85] = (byte)(machine >> 8);
        return data;
    }

    private string Zip(string name, params (string Entry, byte[] Bytes)[] entries)
    {
        var path = Path.Combine(_root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (entry, bytes) in entries)
        {
            using var s = zip.CreateEntry(entry).Open();
            s.Write(bytes);
        }
        return path;
    }

    [Fact]
    public void ZipHasX64Bin_AcceptsX64_RejectsArm64Only()
    {
        var x64 = Zip("x64.zip", ("bin/x64/nvngx_dlss.dll", Pe(OperationGuard.MachineAmd64)));
        var nested = Zip("nested.zip", ("streamline-sdk-v2.14.1/bin/x64/nvngx_dlss.dll", Pe(OperationGuard.MachineAmd64)));
        var arm = Zip("arm.zip", ("bin/arm64/nvngx_dlss.dll", Pe(0xAA64)));
        // Correct folder name, wrong bytes: the header is the authority, not the path.
        var liar = Zip("liar.zip", ("bin/x64/nvngx_dlss.dll", Pe(0xAA64)));

        Assert.True(StreamlineDownloadService.ZipHasX64Bin(x64));
        Assert.True(StreamlineDownloadService.ZipHasX64Bin(nested));
        Assert.False(StreamlineDownloadService.ZipHasX64Bin(arm));
        Assert.False(StreamlineDownloadService.ZipHasX64Bin(liar));

        // Order-independent: a non-x64 match listed first must not hide a valid x64 one.
        var mixed = Zip("mixed.zip",
            ("other/bin/x64/nvngx_dlss.dll", Pe(0xAA64)),
            ("bin/x64/nvngx_dlss.dll", Pe(OperationGuard.MachineAmd64)));
        Assert.True(StreamlineDownloadService.ZipHasX64Bin(mixed));
    }

    /// <summary>The name DownloadLatestAsync writes to the cache must pass the same filter.</summary>
    [Fact]
    public void CachedZipName_RoundTripsThroughFilter()
    {
        var src = SrcFile("DLSSVersionToolkit.Core", "Services", "StreamlineDownloadService.cs");
        Assert.Contains("var fileName = $\"streamline-sdk-{latest.Version}.zip\";", src);
        Assert.True(StreamlineDownloadService.IsX64SdkAssetName("streamline-sdk-2.14.1.zip"));
        Assert.Equal("2.14.1", StreamlineDownloadService.ParseVersionFromZipName("streamline-sdk-2.14.1.zip"));
    }

    [Fact]
    public void ReadPeMachine_ReadsCoffMachine()
    {
        Assert.Equal(OperationGuard.MachineAmd64, OperationGuard.ReadPeMachine(Pe(OperationGuard.MachineAmd64)));
        Assert.Equal((ushort)0xAA64, OperationGuard.ReadPeMachine(Pe(0xAA64)));
        Assert.Null(OperationGuard.ReadPeMachine(new byte[1024]));
    }

    /// <summary>The recursive "any nvngx_dlss.dll anywhere" fallback found bin/arm64. It stays gone.</summary>
    [Fact]
    public void StreamlineBinLookup_HasNoRecursiveFallback()
    {
        var src = SrcFile("DLSSVersionToolkit.Core", "Services", "StreamlineDownloadService.cs");
        Assert.DoesNotContain("\"nvngx_dlss.dll\", SearchOption.AllDirectories", src);
    }

    // ---- NGX row scan order ------------------------------------------------------------

    [Fact]
    public void OrderForRowScan_WriteRootFirst_NoDuplicates()
    {
        var candidates = new[] { @"D:\OtaCache", @"C:\ProgramData\NVIDIA\NGX", @"E:\Other" };

        var ordered = ScanService.OrderForRowScan(candidates, @"C:\ProgramData\NVIDIA\NGX\");

        Assert.Equal(@"C:\ProgramData\NVIDIA\NGX\", ordered[0]);
        Assert.Equal(3, ordered.Count);
        Assert.Equal(new[] { @"D:\OtaCache", @"E:\Other" }, ordered.Skip(1));
    }

    [Fact]
    public void OrderForRowScan_NoWriteRoot_KeepsOriginalOrder()
    {
        var candidates = new[] { "a", "b" };
        Assert.Equal(candidates, ScanService.OrderForRowScan(candidates, null));
    }

    /// <summary>Write and read must agree on which root is "NGX Release" — one ordering helper, two callers.</summary>
    [Fact]
    public void SyncToNgx_UsesSameOrderingAsScan()
    {
        var src = SrcFile("DLSSVersionToolkit.Core", "Services", "UpgradeService.cs");
        Assert.Contains("ScanService.OrderForRowScan(", src);
    }

    // ---- Override config read-back -----------------------------------------------------

    [Fact]
    public void ParseOverrideState_ToolkitBlock_IsActive()
    {
        var s = AnWaveAutoService.ParseOverrideState(
            "[dlss_override]\r\napp_E658700_force = 1\r\napp_E658700 = 310.9.1.0\r\n\r\n[streamline_override]\r\napp_E658703_force = 1\r\n");
        Assert.True(s.IsActive);
        Assert.Equal("310.9.1.0", s.DlssVersion);
    }

    [Theory]
    [InlineData("[dlss_override]\napp_E658700_force = 0\napp_E658700 = 310.9.1.0\n")]   // not forced
    [InlineData("[dlss_override]\napp_E658700_force = 1\n")]                             // no version
    [InlineData("[other]\napp_E658700_force = 1\napp_E658700 = 310.9.1.0\n")]            // wrong section
    [InlineData("; [dlss_override]\n; app_E658700_force = 1\n")]                        // commented out
    public void ParseOverrideState_NotActive(string content) =>
        Assert.False(AnWaveAutoService.ParseOverrideState(content).IsActive);

    [Fact]
    public void ReadOverrideState_MissingFile_NotActive()
    {
        var s = AnWaveAutoService.ReadOverrideState(Path.Combine(_root, "absent.txt"));
        Assert.False(s.ConfigExists);
        Assert.False(s.IsActive);
    }

    /// <summary>The unconditional "DLSS Override is now globally active." literal must not return.</summary>
    [Fact]
    public void SuccessDialog_ReadsBackOverrideState()
    {
        var vm = SrcFile("DLSSVersionToolkit", "ViewModels", "MainViewModel.cs");
        Assert.DoesNotContain("\"DLSS Override is now globally active.", vm);
        Assert.DoesNotContain("\"DLSS Override has been activated globally.", vm);
        Assert.Contains("BuildOverrideActivationLine(", vm);
    }

    // ---- Stale results -----------------------------------------------------------------

    /// <summary>
    /// The drop-on-busy guard was the stale-results root cause: SyncAsync's rescan and the
    /// post-Update-All rescan were silently discarded. Refreshes queue on a gate instead.
    /// </summary>
    [Fact]
    public void ScanAsync_QueuesInsteadOfDropping()
    {
        var vm = SrcFile("DLSSVersionToolkit", "ViewModels", "MainViewModel.cs");
        var at = vm.IndexOf("private async Task ScanAsync()", StringComparison.Ordinal);
        Assert.True(at > 0);
        var head = vm.Substring(at, 400);
        Assert.DoesNotContain("if (IsScanning) return;", head);
        Assert.Contains("_scanGate.WaitAsync()", head);
        Assert.Contains("_scanGate.Release()", vm);
        // Two owners, each clears only its own flag (reviewer finding: a restored snapshot wedged
        // the UI in "Scanning..." when a queued scan finished after SyncAsync).
        Assert.DoesNotContain("ownerHeldScanning", vm);
        Assert.Contains("IsScanning = _syncActive;", vm);
        Assert.Contains("IsScanning = _scanActive;", vm);
    }

    // ---- Reset baseline ----------------------------------------------------------------

    private (OverrideResetService Svc, string ConfigPath) Reset()
    {
        var configPath = Path.Combine(_root, "nvngx_config.txt");
        var manifest = new OverrideManifestService(new VersionComparer(), _root);
        return (new OverrideResetService(manifest, _root, configPath), configPath);
    }

    private const string ToolkitConfig =
        "[dlss_override]\napp_E658700_force = 1\napp_E658700 = 310.9.1.0\n\n[streamline_override]\napp_E658703_force = 1\n";

    [Fact]
    public void Reset_RestoresUserConfigCapturedBeforeToolkit()
    {
        var (svc, cfg) = Reset();
        File.WriteAllText(cfg, "[user]\nmine = 1\n");

        Assert.True(svc.EnsureBaselineCaptured(indicatorRawValue: null));
        File.WriteAllText(cfg, ToolkitConfig);            // toolkit writes its override

        var result = svc.ResetFiles();

        Assert.True(result.AllSucceeded);
        Assert.Equal("[user]\nmine = 1\n", File.ReadAllText(cfg));
        Assert.NotNull(result.BackupFolder);             // Reset itself is undoable
        Assert.Equal(ToolkitConfig, File.ReadAllText(Path.Combine(result.BackupFolder!, "nvngx_config.txt")));
    }

    [Fact]
    public void Reset_RemovesConfigTheToolkitCreated()
    {
        var (svc, cfg) = Reset();
        svc.EnsureBaselineCaptured(null);                 // nothing existed
        File.WriteAllText(cfg, ToolkitConfig);

        Assert.True(svc.ResetFiles().AllSucceeded);
        Assert.False(File.Exists(cfg));
    }

    /// <summary>A pre-v0.76 user already has the toolkit's block on disk; it is not "original".</summary>
    [Fact]
    public void Baseline_NeverCapturesToolkitBlockAsOriginal()
    {
        var (svc, cfg) = Reset();
        File.WriteAllText(cfg, ToolkitConfig);

        svc.EnsureBaselineCaptured(indicatorRawValue: 1024);

        var b = svc.LoadBaseline()!;
        Assert.Null(b.NgxConfigText);
        Assert.Null(b.IndicatorRawValue);                 // 1024 is the value this app writes
    }

    [Fact]
    public void Baseline_IsCapturedOnce()
    {
        var (svc, cfg) = Reset();
        File.WriteAllText(cfg, "[user]\nfirst = 1\n");
        Assert.True(svc.EnsureBaselineCaptured(null));

        File.WriteAllText(cfg, "[user]\nsecond = 1\n");
        Assert.False(svc.EnsureBaselineCaptured(null));

        Assert.Equal("[user]\nfirst = 1\n", svc.LoadBaseline()!.NgxConfigText);
    }

    /// <summary>Reviewer finding: a failed capture must be recorded, never retried into a false "absent".</summary>
    [Fact]
    public void Baseline_CaptureFailure_IsRecordedNotRetried()
    {
        var (svc, cfg) = Reset();
        File.WriteAllText(cfg, "[user]\nmine = 1\n");
        using (new FileStream(cfg, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.True(svc.EnsureBaselineCaptured(null));      // locked -> unreadable

        Assert.True(svc.LoadBaseline()!.ConfigCaptureFailed);
        Assert.False(svc.EnsureBaselineCaptured(null));          // no second capture

        // User's own file (not toolkit-written) survives Reset when the original is unknown.
        Assert.True(svc.ResetFiles().AllSucceeded);
        Assert.Equal("[user]\nmine = 1\n", File.ReadAllText(cfg));
    }

    [Fact]
    public void Reset_ClearsImportedOverrideRecords()
    {
        var (svc, _) = Reset();
        var manifest = new OverrideManifestService(new VersionComparer(), _root);
        manifest.RecordImport("nvngx_dlss.dll", "310.9.1.0", @"C:\lib\nvngx_dlss.dll", "4f0901", staging: false);

        svc.ResetFiles();

        Assert.Empty(new OverrideManifestService(new VersionComparer(), _root).Load().Overrides);
    }

    /// <summary>Reset must ask first and name what it changes.</summary>
    [Fact]
    public void ResetCommand_ConfirmsBeforeActing()
    {
        var vm = SrcFile("DLSSVersionToolkit", "ViewModels", "MainViewModel.cs");
        var at = vm.IndexOf("private async Task ResetOverridesAsync()", StringComparison.Ordinal);
        Assert.True(at > 0);
        var body = vm[at..];
        var confirm = body.IndexOf("MessageBoxButton.OKCancel", StringComparison.Ordinal);
        var firstWrite = body.IndexOf("ApplyPresetAsync(", StringComparison.Ordinal);
        Assert.True(confirm > 0 && firstWrite > confirm, "Reset must confirm before its first write");
    }

    // ---- Settings snapshot -------------------------------------------------------------

    [Fact]
    public void SettingsDialog_SnapshotsOnOpen_AndOffersRestore()
    {
        var cs = SrcFile("DLSSVersionToolkit", "Views", "SettingsDialog.xaml.cs");
        var xaml = SrcFile("DLSSVersionToolkit", "Views", "SettingsDialog.xaml");
        Assert.Contains("SettingsService.WriteSnapshotAsync(", cs);
        Assert.Contains("RestorePrevious_Click", xaml);
    }
}
