using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.0.57 sibling-sweep gates. Each pins one fix from the class-based audit: the same defect
/// had shipped in a sibling before (detector/applier divergence v0.0.43→v0.0.56, root literals
/// x7, Debug-only failures x5 releases), so these exist to make the NEXT recurrence red in CI.
///
/// Red/green arming note: macOS cannot compile this solution — these were armed by porting each
/// predicate to Python against `git archive` trees of v0.0.54/v0.0.56 before spending CI.
/// </summary>
public class SiblingSweepTests
{
    // ------------------------------------------------------------------
    // C2: "is X newer than Y" has ONE definition. UpgradeService used to carry
    // a private IsVersionNewer lacking VersionComparer's pad-to-4, so 2-part
    // versions read as "never newer" on the sync path only. The deletion is
    // pinned by scanning the source: no private version predicate may return.
    // ------------------------------------------------------------------

    [Fact]
    public void UpgradeService_DelegatesComparison_ToTheSharedComparer()
    {
        var srcRoot = FindRepoSubdir("src");
        var path = Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services", "UpgradeService.cs");
        var text = File.ReadAllText(path);

        Assert.DoesNotContain("private static bool IsVersionNewer", text,
            "UpgradeService must not re-implement version comparison — inject IVersionComparer " +
            "(the private copy lacked pad-to-4 and diverged from every other consumer)");
        Assert.Contains("_versionComparer.IsNewer(", text,
            "sync decisions must go through the shared IVersionComparer");
        Assert.DoesNotContain("new UpgradeService(new NgxScanner(new NgxConfigParser()), new BackupService())",
            text + File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
                "DlssDownloadService.cs")) +
            File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
                "StreamlineDownloadService.cs")),
            "composition sites must supply the shared comparer");
    }

    [Fact]
    public void VersionComparer_PadsShortVersions_SoTwoPartCompareWorks()
    {
        var c = new VersionComparer();
        Assert.True(c.IsNewer("310.7", "310.6"), "2-part versions must compare numerically");
        Assert.False(c.IsNewer("310.6", "310.10"), "lexical order trap: 6 < 10");
        Assert.True(c.IsNewer("310.10", "310.9"));
        Assert.False(c.IsNewer("310.6", "310.6"));
    }

    // ------------------------------------------------------------------
    // C3: the same-version resync guard used to check ONLY nvngx_dlss.dll while
    // PerformSync writes all four NgxDllNames — missing dlssg/dlssd/deepdvc
    // never triggered recreation.
    // ------------------------------------------------------------------

    [Fact]
    public void MissingNgxDllNames_ReportsEveryAbsentCanonicalDll_NotJustTheMainOne()
    {
        var dir = Directory.CreateTempSubdirectory("dlssvt-missing");
        try
        {
            // Only the main DLL present — the three components are missing and MUST be named.
            File.WriteAllText(Path.Combine(dir.FullName, "nvngx_dlss.dll"), "mz");

            var missing = UpgradeService.MissingNgxDllNames(dir.FullName);

            Assert.Equal(3, missing.Count);
            Assert.Contains("nvngx_dlssg.dll", missing);
            Assert.Contains("nvngx_dlssd.dll", missing);
            Assert.Contains("nvngx_deepdvc.dll", missing);
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    [Fact]
    public void MissingNgxDllNames_EmptyWhenAllPresent()
    {
        var dir = Directory.CreateTempSubdirectory("dlssvt-full");
        try
        {
            foreach (var n in UpgradeService.NgxDllNames)
                File.WriteAllText(Path.Combine(dir.FullName, n), "mz");

            Assert.Empty(UpgradeService.MissingNgxDllNames(dir.FullName));
        }
        finally
        {
            Directory.Delete(dir.FullName, recursive: true);
        }
    }

    // ------------------------------------------------------------------
    // C1/C6: the AnWave override config lives at exactly ONE address, defined
    // by the resolver; and its version comes from DLL bytes, not the release URL.
    // ------------------------------------------------------------------

    [Fact]
    public void ConfigFilePath_IsDefinedOnlyInTheResolver()
    {
        var srcRoot = FindRepoSubdir("src");
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            if (Path.GetFileName(file) == "NgxPathResolver.cs")
                continue;
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains("\"NVIDIA\", \"NGX\", \"nvngx_config.txt\"", StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
        }

        Assert.True(offenders.Count == 0,
            "nvngx_config.txt location rebuilt outside NgxPathResolver at: " +
            string.Join(", ", offenders) + ". Use NgxPathResolver.GetConfigFilePath().");
    }

    [Fact]
    public void GetConfigFilePath_EndsWithConfigName_UnderAWriteRoot()
    {
        var cfg = NgxPathResolver.GetConfigFilePath();

        Assert.EndsWith("nvngx_config.txt", cfg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(NgxPathResolver.WriteRoots[0], cfg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SetupAnWave_VersionSource_IsDllBytes_WithUrlFallbackOnly()
    {
        var srcRoot = FindRepoSubdir("src");
        var text = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "AnWaveAutoService.cs"));

        Assert.Contains("ReadDlssVersionFromFolder(InstallDir)", text,
            "the override config version must be read back from the copied DLL bytes");
        Assert.Matches(
            @"dllBytesVersion\)\s*\?\s*urlVersion\s*:\s*dllBytesVersion",
            text,
            "URL-derived version may survive only as an explicit last-resort fallback");
    }

    // ------------------------------------------------------------------
    // C4/C5/C7/C10: non-fatal steps must still be reported. Post-copy verify
    // failures and per-profile write skips count into results; scan errors land
    // in ScanResult.Errors. Pinned by source shape because the paths need a
    // live NVIDIA driver / network to exercise for real.
    // ------------------------------------------------------------------

    [Fact]
    public void AutoApplySync_FailedVerify_IsCountedNotSwallowed()
    {
        var srcRoot = FindRepoSubdir("src");
        var text = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "AnWaveAutoService.cs"));

        Assert.Contains("result.FailedFiles.Add", text,
            "skipped DLLs must be recorded on the result, not vanish into Debug.WriteLine");
        Assert.Contains("FailedFiles { get; set; }", text,
            "AnWaveAutoApplyResult must carry the skip list");
    }

    [Fact]
    public void StreamlineDownload_VerifiesBeforeCaching()
    {
        var srcRoot = FindRepoSubdir("src");
        var sl = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "StreamlineDownloadService.cs"));

        // The verify gate must sit BETWEEN the copy into the cache path and the cache assignment.
        // LastIndexOf: the service has an early-return path that also assigns _cachedDownloadPath;
        // the gate guards the main download flow's assignment.
        var verifyAt = sl.IndexOf("OperationGuard.VerifyFile(destPath, totalRead)", StringComparison.Ordinal);
        var cacheAt = sl.LastIndexOf("_cachedDownloadPath = destPath;", StringComparison.Ordinal);
        Assert.True(verifyAt >= 0 && cacheAt > verifyAt,
            "Streamline downloads must pass size verification BEFORE being cached (a truncated " +
            "zip used to sit in the cache and fail later at extract time)");

        var dlss = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "DlssDownloadService.cs"));
        Assert.Contains("OperationGuard.VerifyFile(destPath, totalRead)", dlss,
            "the DLSS twin is the reference implementation of this gate");
    }

    [Fact]
    public void PresetSkips_AreCountedIntoTheResult()
    {
        var srcRoot = FindRepoSubdir("src");
        var svc = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "PresetOverrideService.cs"));

        Assert.Contains("int ProfilesSkipped = 0", svc,
            "PresetOverrideResult must carry the skip count");
        Assert.Contains("profilesSkipped++", svc,
            "each skipped profile must increment the counter");
    }

    [Fact]
    public void ScanFailures_LandInTheScanResult()
    {
        var srcRoot = FindRepoSubdir("src");
        var scanner = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "NgxScanner.cs"));
        var scanService = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "ScanService.cs"));

        Assert.Contains("errors?.Add(", scanner,
            "scanner catch blocks must surface failures when an error sink is supplied");
        Assert.Contains("_ngxScanner.Scan(path, scanErrors)", scanService,
            "ScanService must collect scanner errors into result.Errors");
    }

    // ------------------------------------------------------------------
    // C8: Evaluate's installedByDll came from _lastScanResult, permanently empty.
    // ------------------------------------------------------------------

    [Fact]
    public void ReassertOverrides_PopulatesInstalledFromScanTruth()
    {
        var srcRoot = FindRepoSubdir("src");
        var vm = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit", "ViewModels",
            "MainViewModel.cs"));

        // The dict handed to Evaluate must be populated from the NGX_Release scan entry.
        var evalAt = vm.IndexOf("Evaluate(installedByDll", StringComparison.Ordinal);
        var populateAt = vm.IndexOf("installedByDll[\"nvngx_deepdvc.dll\"] = ngxEntry.DeepDVC;", StringComparison.Ordinal);
        Assert.True(populateAt >= 0 && evalAt > populateAt,
            "installedByDll must be populated from the last scan BEFORE Evaluate consumes it");
    }

    /// <summary>Walks up from the test binary to the repo root and returns the src/ subdir.</summary>
    internal static string FindRepoSubdir(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException($"Could not locate '{name}' above {AppContext.BaseDirectory}");
    }
}
