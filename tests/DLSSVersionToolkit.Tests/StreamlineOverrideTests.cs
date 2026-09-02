using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Streamline plugin override (v0.67) — reverse-engineered from nvidiaDlssGlom:
/// payloads at models\sl_&lt;plugin&gt;_0\versions\&lt;packed&gt;\files\160_E658703.dll,
/// activated by [sl_&lt;plugin&gt;_0] / app_E658703 = &lt;version&gt; sections in nvngx_config.txt.
/// Ground truth: SimonMacer/AnWave issue #66 full discovery log (2025-08), Crimson Desert
/// modding walkthrough (2026-04), MSFS crash report (2025-05), and the literal config
/// templates + dir->DLL table extracted from nvidiaDlssGlom.exe v2.8.24.13 (#Strings heap,
/// 2026-09). These gates pin the RE against drift: rename the dir form, change the extension,
/// or drop the E658703-only rule and CI reddens.
/// </summary>
public class StreamlineOverrideTests
{
    [Fact]
    public void PluginMap_MatchesObservedGlomTable()
    {
        // The 11 plugin dirs from glom's table that an SDK actually supplies DLLs for
        // (verified against Streamline SDK v2.12.0 bin/x64, downloaded 2026-09-02).
        var expected = new[]
        {
            "sl_common_0", "sl_deepdvc_0", "sl_directsr_0", "sl_dlss_0", "sl_dlss_d_0",
            "sl_dlss_g_0", "sl_interposer_0", "sl_nis_0", "sl_nvperf_0", "sl_pcl_0",
            "sl_reflex_0",
        };
        Assert.Equal(expected.OrderBy(x => x),
            NgxModelLayout.StreamlinePluginDirByDll.Values.OrderBy(x => x));
        Assert.All(NgxModelLayout.StreamlinePluginDirByDll.Keys,
            k => Assert.StartsWith("sl.", k, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PayloadFileName_IsE658703Dll_NotBin()
    {
        // The one observed form: 160_E658703.dll. NOT .bin, and no E658700 twin was ever
        // observed for streamline plugins — exactly-what-was-observed policy (same rule that
        // keeps OverrideTreeDlls from guessing).
        var names = NgxModelLayout.GetStreamlinePayloadFileNames().ToList();
        Assert.Equal(new[] { "160_E658703.dll" }, names);
    }

    [Fact]
    public void SyncPlugins_RejectsNonPeAndMissingVersion_WithoutThrowing()
    {
        var source = Directory.CreateTempSubdirectory("slsrc");
        var ngx = Directory.CreateTempSubdirectory("slngx");
        try
        {
            File.WriteAllText(Path.Combine(source.FullName, "sl.common.dll"), "MZnotape");
            File.WriteAllText(Path.Combine(source.FullName, "sl.pcl.dll"), "MZnotape2");

            var outcome = StreamlineOverrideService.SyncPlugins(source.FullName, ngx.FullName);

            // Both fakes must surface in Skipped (never silent), nothing written.
            Assert.Empty(outcome.Written);
            Assert.Equal(2, outcome.Skipped.Count);
            Assert.All(outcome.Skipped, s => Assert.Contains("PE signature check failed", s));
        }
        finally
        {
            Directory.Delete(source.FullName, true);
            Directory.Delete(ngx.FullName, true);
        }
    }

    [Fact]
    public void InstalledPlugins_DecodesNewestPacked_WithPayloadOnly()
    {
        var ngx = Directory.CreateTempSubdirectory("slinst");
        try
        {
            // 2.12.0 (134144) payload present; 2.11.0 (133888) older; interposer folder exists
            // with a version folder but NO payload -> must not count as installed.
            var newer = Path.Combine(ngx.FullName, "models", "sl_common_0", "versions", "134144", "files");
            var older = Path.Combine(ngx.FullName, "models", "sl_common_0", "versions", "133888", "files");
            var empty = Path.Combine(ngx.FullName, "models", "sl_interposer_0", "versions", "134144", "files");
            Directory.CreateDirectory(newer);
            Directory.CreateDirectory(older);
            Directory.CreateDirectory(empty);
            File.WriteAllText(Path.Combine(newer, "160_E658703.dll"), "payload");

            var installed = StreamlineOverrideService.InstalledPlugins(ngx.FullName);

            var common = Assert.Single(installed, i => i.ComponentDir == "sl_common_0");
            Assert.Equal("2.12.0", common.Version);   // newest wins, decoded from folder name
            Assert.DoesNotContain(installed, i => i.ComponentDir == "sl_interposer_0");
        }
        finally
        {
            Directory.Delete(ngx.FullName, true);
        }
    }

    /// <summary>
    /// The config writer must emit glom's observed activation form verbatim. WriteNgXConfig is
    /// private and the test assembly has no InternalsVisibleTo, so the shape is pinned from
    /// source — the same pattern as the other wiring gates, and the exact regression surface
    /// (section header spelling / key name / which appId) the RE depends on.
    /// </summary>
    [Fact]
    public void WriteNgXConfig_EmitsObservedPluginActivationTemplate()
    {
        var src = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit.Core", "Services", "AnWaveAutoService.cs"));

        Assert.Contains("[{p.ComponentDir}]", src);
        Assert.Contains("app_E658703 = {p.Version}", src);
        // Section content keyed off ON-DISK plugins, not the config's own state.
        Assert.Contains("StreamlineOverrideService.InstalledPlugins(", src);
    }

    /// <summary>
    /// The plugin sync must run on the Streamline path AFTER the nvngx verification succeeds —
    /// same ordering discipline as the v0.66 retention gate: a wiring regression here would
    /// silently stop activating streamline overrides while every test still passed.
    /// </summary>
    [Fact]
    public void PerformSync_SyncsPlugins_AfterNvngxVerify()
    {
        var src = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit.Core", "Services", "UpgradeService.cs"));

        var a = src.IndexOf("private UpgradeOperation PerformSync(", StringComparison.Ordinal);
        Assert.True(a > 0);
        var body = src.Substring(a);
        var nextMethod = body.IndexOf("\n    private ", 10, StringComparison.Ordinal);
        if (nextMethod > 0) body = body.Substring(0, nextMethod);

        Assert.Contains("StreamlineOverrideService.SyncPlugins(", body);
        Assert.True(body.IndexOf("VerifyCopiedFiles(binPath", StringComparison.Ordinal)
                  < body.IndexOf("StreamlineOverrideService.SyncPlugins(", StringComparison.Ordinal),
            "plugin sync must run after the nvngx post-copy verification");
    }
}
