using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.68 — "every cell reads its own DLL".
///
/// The INSTALLED VERSIONS grid used to take NGX rows from nvngx_package_config.txt text
/// (NgxConfigParser.ParseComponent), with a DLL read layered on top for only FOUR of the five
/// components — DLSSNR had no DLL read at all, and a component whose DLL was absent inherited
/// whatever the config still claimed. That is the Image-1 report: AnWave showing DLSS 310.7.0.0
/// beside Frame Gen / DLSSD 310.6.0.0, DeepDVC "Unknown", and versions that reappear after they
/// should be gone.
///
/// These gates pin the rule the repo has stated since v0.0.53 but never enforced:
/// DLL bytes are the only version authority.
/// </summary>
public class VersionTruthTests
{
    private const string ConfigName = "nvngx_package_config.txt";

    /// <summary>
    /// A version folder with a stale config and a NEWER DLL must report the DLL. This is the
    /// exact Image-1 defect: config says 310.6.0.0, bytes say something else, grid showed the
    /// config. RED against v0.67 (config value won for any component the DLL read missed).
    /// </summary>
    [Fact]
    public void StaleConfig_LosesToDllBytes()
    {
        var dir = Directory.CreateTempSubdirectory("ngxtruth");
        try
        {
            // The config claims 310.6.0.0 for every component.
            File.WriteAllText(Path.Combine(dir.FullName, ConfigName),
                "dlss, 310.6.0.0\ndlssg, 310.6.0.0\ndlssd, 310.6.0.0\ndeepdvc, 310.6.0.0\ndlssnr, 310.6.0.0\n");

            // ...but no DLLs exist. A component with no file must NOT inherit the config's claim.
            var result = new NgxConfigParser().Parse(dir.FullName);

            Assert.Equal(NgxConfigParser.VersionAbsent, result.DLSS);
            Assert.Equal(NgxConfigParser.VersionAbsent, result.FrameGen);
            Assert.Equal(NgxConfigParser.VersionAbsent, result.DLSSD);
            Assert.Equal(NgxConfigParser.VersionAbsent, result.DeepDVC);
            Assert.Equal(NgxConfigParser.VersionAbsent, result.DLSSNR);

            // The config's claim is still available as activation state — just never as a version.
            Assert.True(result.ConfigNamesComponents);
        }
        finally { Directory.Delete(dir.FullName, true); }
    }

    /// <summary>
    /// Status codes are three distinct facts and must not collapse. Missing file => "—";
    /// present-but-unreadable => "Unknown". Before v0.68 both read "Unknown", so the grid could
    /// not distinguish "not installed" from "installed but broken".
    /// </summary>
    [Fact]
    public void PresentButUnreadable_IsUnknown_MissingIsEmDash()
    {
        var dir = Directory.CreateTempSubdirectory("ngxstatus");
        try
        {
            // Present but not a real PE -> version resource unreadable.
            File.WriteAllText(Path.Combine(dir.FullName, "nvngx_dlss.dll"), "not a PE file");

            var result = new NgxConfigParser().Parse(dir.FullName);

            Assert.Equal(NgxConfigParser.VersionUnreadable, result.DLSS);   // file there, unreadable
            Assert.Equal(NgxConfigParser.VersionAbsent, result.FrameGen);   // no file at all
            Assert.NotEqual(result.DLSS, result.FrameGen);                  // the two must differ
        }
        finally { Directory.Delete(dir.FullName, true); }
    }

    /// <summary>
    /// All five components must be read, each from its own file. DLSSNR was omitted from the
    /// DLL-read block through v0.67 while the other four were present — a gap invisible to any
    /// test that only checked "does the grid have a DLSSNR column".
    /// </summary>
    [Fact]
    public void EveryComponent_HasItsOwnDllRead()
    {
        var src = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit.Core", "Services", "NgxConfigParser.cs"));

        foreach (var dll in new[] { "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll",
                                    "nvngx_dlssnr.dll", "nvngx_deepdvc.dll" })
            Assert.Contains(dll, src);

        // The read must go through the canonical reader, not a local FileVersionInfo call.
        Assert.Contains("DllVersionReader.ReadComponentVersion(", src);
    }

    /// <summary>
    /// The config parser must not assign a parsed config value to any version field. This is the
    /// structural gate behind the whole fix: it fails if a future change reintroduces a non-DLL
    /// version source. RED against v0.67 (which had result.DLSS = ParseComponent(...)).
    /// </summary>
    [Fact]
    public void ConfigParse_NeverFeedsAVersionField()
    {
        var src = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit.Core", "Services", "NgxConfigParser.cs"));

        foreach (var field in new[] { "DLSS", "FrameGen", "DLSSD", "DeepDVC", "DLSSNR" })
            Assert.DoesNotContain($"result.{field} = ParseComponent(", src);
    }

    /// <summary>
    /// Status codes must never be treated as comparable versions. Callers used to test only
    /// != "Unknown", so "N/A" and the new "—" would have sailed through as real versions and
    /// been fed to the comparer.
    /// </summary>
    [Theory]
    [InlineData("Unknown", false)]
    [InlineData("N/A", false)]
    [InlineData("—", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("310.7.128.0", true)]
    [InlineData("2.12.0", true)]
    public void IsReportedVersion_RejectsEveryStatusCode(string value, bool expected)
        => Assert.Equal(expected, DllVersionReader.IsReportedVersion(value));

    /// <summary>
    /// No caller may re-derive "is this a real version" with a bare "Unknown" literal — one rule,
    /// one predicate. Scanners keep their local != "Unknown" checks on their OWN read result,
    /// which is a different question ("did my read succeed"), so only the consuming surfaces are
    /// scanned here.
    /// </summary>
    [Fact]
    public void NoConsumer_ReimplementsTheStatusCheck()
    {
        var offenders = new List<string>();
        foreach (var rel in new[]
        {
            Path.Combine("DLSSVersionToolkit.Core", "Services", "VersionComparer.cs"),
            Path.Combine("DLSSVersionToolkit.Core", "Services", "AnWaveAutoService.cs"),
            Path.Combine("DLSSVersionToolkit", "ViewModels", "MainViewModel.cs"),
        })
        {
            var path = Path.Combine(SiblingSweepTests.FindRepoSubdir("src"), rel);
            var text = File.ReadAllText(path);
            if (text.Contains("== \"Unknown\"") || text.Contains("!= \"Unknown\""))
                offenders.Add(rel);
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} consumer(s) still test the status code by literal: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// Column order and header, pinned: Source | Build ID | DLSS | Frame Gen | DLSSD | DLSS NR |
    /// DeepDVC | Streamline | Override.
    /// </summary>
    [Fact]
    public void Grid_PlacesDlssNrBetweenDlssdAndDeepDvc()
    {
        var xaml = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit", "MainWindow.xaml"));

        var dlssd = xaml.IndexOf("Header=\"DLSSD\"", StringComparison.Ordinal);
        var nr = xaml.IndexOf("Header=\"DLSS NR\"", StringComparison.Ordinal);
        var deep = xaml.IndexOf("Header=\"DeepDVC\"", StringComparison.Ordinal);

        Assert.True(dlssd > 0 && nr > 0 && deep > 0, "all three columns must exist");
        Assert.True(dlssd < nr && nr < deep,
            "DLSS NR must render between DLSSD and DeepDVC");
    }
}
