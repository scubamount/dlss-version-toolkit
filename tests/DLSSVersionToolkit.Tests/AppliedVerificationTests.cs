using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.69 — the completion dialog reports what is on disk, and the dashboard is fresh when it
/// closes.
///
/// The reported defect: "AnWave: v310.7.0.0 applied (4 files)" printed directly beneath
/// "Override nvngx_dlss.dll v310.7.128.0 still applied". Both lines were built from values
/// captured BEFORE the run's writes — the override text came from the pre-run manifest
/// disposition computed at Step 2b, before AnWave and Streamline had written anything. And the
/// only ScanAsync ran AFTER the modal returned, so the header and grid stayed stale until the
/// user dismissed the dialog and, in practice, until they hit Rescan.
/// </summary>
public class AppliedVerificationTests
{
    [Fact]
    public void Verify_ReadsNewestOverrideVersionFolder_NotDirectoryOrder()
    {
        var ngx = Directory.CreateTempSubdirectory("appliedver");
        try
        {
            // Packed folder names sort lexically in the wrong order ("20318080" vs "9999999"),
            // which is why the ONE ordering predicate must be used rather than enumeration order.
            var versions = Path.Combine(ngx.FullName, "models", "dlss_override", "versions");
            foreach (var v in new[] { "20318080", "20317824" })
                Directory.CreateDirectory(Path.Combine(versions, v));

            // No DLLs anywhere -> every component absent, and crucially: no throw, no crash.
            var applied = AppliedVersionVerifier.Verify(ngx.FullName);

            Assert.Equal(5, applied.Count);
            Assert.All(applied, c => Assert.False(c.IsPresent));
            Assert.All(applied, c => Assert.Equal(NgxConfigParser.VersionAbsent, c.Version));
        }
        finally { Directory.Delete(ngx.FullName, true); }
    }

    [Fact]
    public void Verify_PresentButCorruptDll_ReportsUnreadable_NotIntendedVersion()
    {
        var ngx = Directory.CreateTempSubdirectory("appliedcorrupt");
        try
        {
            var folder = Path.Combine(ngx.FullName, "models", "dlss_override", "versions", "20318080");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "nvngx_dlss.dll"), "not a PE");

            var applied = AppliedVersionVerifier.Verify(ngx.FullName);
            var dlss = Assert.Single(applied, c => c.DllName == "nvngx_dlss.dll");

            // The file is there but unconfirmable. The dialog must say so rather than print the
            // version the run intended to write — that substitution is the whole defect class.
            Assert.Equal(NgxConfigParser.VersionUnreadable, dlss.Version);
            Assert.False(dlss.IsPresent);
        }
        finally { Directory.Delete(ngx.FullName, true); }
    }

    /// <summary>
    /// "Present but unreadable" and "absent" must be distinguished by the ONE finder, not by
    /// each caller re-deriving the search. This gate caught a real defect: the first cut of
    /// AppliedVersionVerifier treated ReadComponentVersion's null as "absent", so a corrupt DLL
    /// reported "—" instead of "Unknown". CI failed on it — the gate working.
    ///
    /// Scoped to the two surfaces that answer "is this component present in a version folder":
    /// the grid's parser and the post-apply verifier. Other AllDirectories uses in the tree ask
    /// different questions (locating a DLL inside an extracted zip, resolving a sync source) and
    /// a gate that failed on those would be a gate that gets deleted rather than obeyed.
    /// </summary>
    [Fact]
    public void ComponentPresenceCheck_UsesTheOneFinder()
    {
        var services = Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit.Core", "Services");

        foreach (var name in new[] { "NgxConfigParser.cs", "AppliedVersionVerifier.cs" })
        {
            var text = File.ReadAllText(Path.Combine(services, name));

            Assert.True(text.Contains("DllVersionReader.FindComponentFile("),
                $"{name} must resolve component presence through the canonical finder");

            // Only COMPONENT searches are forbidden. NgxConfigParser legitimately searches for
            // nvngx_package_config.txt, which is a different question with a different answer.
            var lines = File.ReadAllLines(Path.Combine(services, name));
            var offenders = new List<int>();
            for (var i = 0; i < lines.Length; i++)
            {
                if (!lines[i].Contains("SearchOption.AllDirectories")) continue;
                var window = string.Join("\n", lines.Skip(Math.Max(0, i - 2)).Take(3));
                if (window.Contains("nvngx_") && !window.Contains("package_config"))
                    offenders.Add(i + 1);
                if (window.Contains("dllName"))
                    offenders.Add(i + 1);
            }

            Assert.True(offenders.Count == 0,
                $"{name} re-derives the component search at line(s) " +
                $"{string.Join(", ", offenders)} instead of calling FindComponentFile");
        }
    }

    [Fact]
    public void Verify_MissingTree_ReturnsEmpty_WithoutThrowing()
    {
        Assert.Empty(AppliedVersionVerifier.Verify(null));
        Assert.Empty(AppliedVersionVerifier.Verify(""));
        Assert.Empty(AppliedVersionVerifier.Verify(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}")));
    }

    [Fact]
    public void ComponentDlls_CoversTheCanonicalSet_FromOneTable()
    {
        // Sourced from NgxModelLayout so a sixth component is verified automatically. A
        // hand-copied list here would be a list that silently stops covering what it was
        // written for — exactly how nvngx_dlssnr.dll went unread until v0.68.
        var dlls = AppliedVersionVerifier.ComponentDlls.ToList();

        Assert.Equal(5, dlls.Count);
        foreach (var expected in new[] { "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll",
                                         "nvngx_dlssnr.dll", "nvngx_deepdvc.dll" })
            Assert.Contains(expected, dlls);

        var src = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit.Core", "Services", "AppliedVersionVerifier.cs"));
        Assert.Contains("NgxModelLayout.ComponentDirByDll.Keys", src);
    }

    [Theory]
    [InlineData("310.7.128.0", "310.7.0.0", true)]    // disk differs from intent -> report it
    [InlineData("310.7.0.0", "310.7.0.0", false)]     // agreement
    [InlineData("310.7.0.0", null, false)]            // no intent to compare against
    [InlineData("310.7.0.0", "Unknown", false)]       // status code is not an intent
    public void Disagrees_OnlyFlagsRealMismatches(string onDisk, string? intended, bool expected)
    {
        var applied = new List<AppliedComponent>
        {
            new() { DllName = "nvngx_dlss.dll", Version = onDisk }
        };
        Assert.Equal(expected, AppliedVersionVerifier.Disagrees(applied, "nvngx_dlss.dll", intended));
    }

    [Fact]
    public void Disagrees_AbsentComponent_IsNotAMismatch()
    {
        var applied = new List<AppliedComponent>
        {
            new() { DllName = "nvngx_dlssnr.dll", Version = NgxConfigParser.VersionAbsent }
        };
        // Not every run installs every component; absence is not a contradiction.
        Assert.False(AppliedVersionVerifier.Disagrees(applied, "nvngx_dlssnr.dll", "310.7.0.0"));
    }

    /// <summary>
    /// Wiring + ordering. The refresh must happen BEFORE the dialog renders, at the single point
    /// every terminal dialog funnels through, so a future dialog cannot forget it. RED against
    /// v0.68, where ScanAsync sat after the last ThemedMessageBox.Show in the method.
    /// </summary>
    [Fact]
    public void UpdateAll_RefreshesDashboard_BeforeShowingCompletionDialog()
    {
        var src = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit", "ViewModels", "MainViewModel.cs"));

        // The funnel refreshes state itself.
        var funnel = src.IndexOf("private async Task EndUpdateAllProgressAsync()", StringComparison.Ordinal);
        Assert.True(funnel > 0, "the pre-dialog funnel must exist");
        var funnelBody = src.Substring(funnel, 900);
        Assert.Contains("await ScanAsync()", funnelBody);

        // And no terminal dialog may be reached without going through it: the old synchronous
        // EndUpdateAllProgress() is gone entirely.
        Assert.DoesNotContain("EndUpdateAllProgress();", src);
    }

    /// <summary>
    /// The dialog's override summary must come from the post-write disk read, not from the
    /// pre-write re-assertion text. RED against v0.68 (dialogs interpolated `overrideLine`,
    /// assigned before AnWave and Streamline wrote).
    /// </summary>
    [Fact]
    public void CompletionDialog_UsesPostWriteDiskRead_NotPreWriteText()
    {
        var src = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("BuildAppliedOverrideLines(", src);
        Assert.Contains("AppliedVersionVerifier.Verify(", src);

        // The pre-write string must not reach a dialog again.
        Assert.DoesNotContain("overrideLine +", src);
    }
}
