using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.0.57 sibling-sweep gates. Each pins one fix from the class-based audit: the same defect
/// had shipped in a sibling before (detector/applier divergence v0.0.43→v0.0.56, root literals
/// x7, Debug-only failures x5 releases), so these exist to make the NEXT recurrence red in CI.
/// Failure reasons live in XML docs above each test — xUnit asserts carry no message argument.
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

    /// <summary>UpgradeService must not re-implement version comparison; sync decisions go
    /// through the injected shared comparer, and every composition site supplies it.</summary>
    [Fact]
    public void UpgradeService_DelegatesComparison_ToTheSharedComparer()
    {
        var srcRoot = FindRepoSubdir("src");
        var text = File.ReadAllText(
            Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services", "UpgradeService.cs"));

        Assert.DoesNotContain("private static bool IsVersionNewer", text);
        Assert.Contains("_versionComparer.IsNewer(", text);

        foreach (var f in new[] { "DlssDownloadService.cs", "StreamlineDownloadService.cs" })
        {
            var s = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services", f));
            Assert.DoesNotContain(
                "new UpgradeService(new NgxScanner(new NgxConfigParser()), new BackupService())", s);
            Assert.Contains("new VersionComparer()", s);
        }
    }

    [Fact]
    public void VersionComparer_PadsShortVersions_SoTwoPartCompareWorks()
    {
        var c = new VersionComparer();
        Assert.True(c.IsNewer("310.7", "310.6"));
        Assert.False(c.IsNewer("310.6", "310.10"));
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
            File.WriteAllText(Path.Combine(dir.FullName, "nvngx_dlss.dll"), "mz");

            var missing = UpgradeService.MissingNgxDllNames(dir.FullName);

            Assert.Equal(4, missing.Count);
            Assert.Contains("nvngx_dlssg.dll", missing);
            Assert.Contains("nvngx_dlssd.dll", missing);
            Assert.Contains("nvngx_deepdvc.dll", missing);
            Assert.Contains("nvngx_dlssnr.dll", missing);
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

        Assert.Empty(offenders);
    }

    [Fact]
    public void GetConfigFilePath_EndsWithConfigName_UnderAWriteRoot()
    {
        var cfg = NgxPathResolver.GetConfigFilePath();

        Assert.EndsWith("nvngx_config.txt", cfg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(NgxPathResolver.WriteRoots[0], cfg, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The override-config version must be read back from the copied DLL bytes;
    /// the URL-derived value survives only as an explicit last-resort fallback.</summary>
    [Fact]
    public void SetupAnWave_VersionSource_IsDllBytes_WithUrlFallbackOnly()
    {
        var srcRoot = FindRepoSubdir("src");
        var text = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "AnWaveAutoService.cs"));

        Assert.Contains("ReadDlssVersionFromFolder(InstallDir)", text);
        Assert.Matches(@"dllBytesVersion\)\s*\?\s*urlVersion\s*:\s*dllBytesVersion", text);
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

        Assert.Contains("result.FailedFiles.Add", text);
        Assert.Contains("FailedFiles { get; set; }", text);
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
        Assert.True(verifyAt >= 0 && cacheAt > verifyAt);

        var dlss = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "DlssDownloadService.cs"));
        Assert.Contains("OperationGuard.VerifyFile(destPath, totalRead)", dlss);
    }

    [Fact]
    public void PresetSkips_AreCountedIntoTheResult()
    {
        var srcRoot = FindRepoSubdir("src");
        var svc = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "PresetOverrideService.cs"));

        Assert.Contains("int ProfilesSkipped = 0", svc);
        Assert.Contains("profilesSkipped++", svc);
    }

    [Fact]
    public void ScanFailures_LandInTheScanResult()
    {
        var srcRoot = FindRepoSubdir("src");
        var scanner = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "NgxScanner.cs"));
        var scanService = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "ScanService.cs"));

        Assert.Contains("errors?.Add(", scanner);
        Assert.Contains("_ngxScanner.Scan(path, scanErrors)", scanService);
    }

    // ------------------------------------------------------------------
    // C8: Evaluate's installedByDict came from _lastScanResult, permanently empty.
    // ------------------------------------------------------------------

    [Fact]
    public void ReassertOverrides_PopulatesInstalledFromScanTruth()
    {
        var srcRoot = FindRepoSubdir("src");
        var vm = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit", "ViewModels",
            "MainViewModel.cs"));

        // The dict handed to Evaluate must be populated from the NGX_Release scan entry.
        var evalAt = vm.IndexOf("Evaluate(installedByDll", StringComparison.Ordinal);
        var populateAt = vm.IndexOf("installedByDll[\"nvngx_deepdvc.dll\"] = ngxEntry.DeepDVC;",
            StringComparison.Ordinal);
        Assert.True(populateAt >= 0 && evalAt > populateAt);
    }

    // ------------------------------------------------------------------
    // C9 (v0.0.61): the v0.0.57 verify-fail→delete fix landed on the DLL loop of
    // ExtractGlomFromCache but not its sibling glom-file loop in the SAME method.
    // A size-corrupt nvidiaDlssGlom.exe passes the File.Exists gate below it, so setup
    // reports success over a broken install. Pin: no VerifyFile failure arm in this file
    // may consist of Debug.WriteLine alone.
    // ------------------------------------------------------------------

    [Fact]
    public void AnWaveVerifyFailures_NeverDebugOnly()
    {
        var srcRoot = FindRepoSubdir("src");
        var lines = File.ReadAllLines(Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services",
            "AnWaveAutoService.cs"));

        // Scan the 4-line arm following every failed-verify check. A body regex cannot be used:
        // interpolated braces inside the Debug.WriteLine argument ($"{destPath}") truncate any
        // [^}]* capture before the File.Delete, making fixed code read as broken.
        var offenders = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].Contains("if (!OperationGuard.VerifyFile(")) continue;
            var arm = string.Join("\n", lines.Skip(i + 1).Take(4));
            if (arm.Contains("Debug.WriteLine") && !arm.Contains("File.Delete")
                && !arm.Contains("ErrorMessage") && !arm.Contains("FailedFiles"))
                offenders++;
        }

        Assert.True(offenders == 0,
            $"{offenders} verify-failure arm(s) log to Debug and count the file anyway");
    }

    // ------------------------------------------------------------------
    // C10 (v0.0.61): the version-validity regex was byte-identical in GlobalScanner,
    // StreamlineScanner, and NgxConfigParser. Divergence = surfaces disagreeing on what
    // a version is. ONE definition (DllVersionReader.IsValidVersion); no private copies.
    // ------------------------------------------------------------------

    [Fact]
    public void VersionValidityRegex_HasOneDefinition()
    {
        var srcRoot = FindRepoSubdir("src");
        var copies = Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("DllVersionReader.cs", StringComparison.Ordinal))
            // Verbatim substring of the SHARED regex prefix — matches both the historical
            // {1,3} copies and any future copy of the current {0,2} canonical. (A regex with
            // doubled backslashes here would match nothing and the gate would pass vacuously.)
            .Where(f => File.ReadAllText(f).Contains(@"\d+\.\d+(\.\d+){", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f))
            .ToList();

        Assert.True(copies.Count == 0,
            "Version-validity regex rebuilt outside DllVersionReader: " + string.Join(", ", copies));

        var canonical = File.ReadAllText(Path.Combine(srcRoot, "DLSSVersionToolkit.Core",
            "Services", "DllVersionReader.cs"));
        Assert.Contains("public static bool IsValidVersion(", canonical);
    }

    /// <summary>Walks up from the test binary to the repo root and returns the src/ subdir.</summary>
    internal static string FindRepoSubdir(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(dir.FullName, "tests")))
                return candidate;
        }
        throw new DirectoryNotFoundException($"Could not locate '{name}' from {AppContext.BaseDirectory}");
    }
}
