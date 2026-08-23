using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Regression tests for the v0.0.50 UI-truth pass. Each pins a defect that was visible in a
/// user's screenshot of the dashboard:
///  1. "YOUR GAMES" showed ~35 raw hex driver-profile IDs, burying the 7 real titles.
///  2. The AnWave row reported Streamline as "Unknown" while NGX rows reported "N/A" for the
///     identical not-applicable fact (one fact, two words, same grid).
///  3. The status card printed "AnWave: 310.7,0,0" because that path read FileVersionInfo
///     directly instead of going through DllVersionReader's comma normalization.
/// </summary>
public class UiTruthRegressionTests
{
    // --- 1. Unnamed-profile predicate: hex IDs hidden, real titles kept ---------------------

    [Theory]
    // Exact strings from the reported screenshot — these MUST be treated as unnamed.
    [InlineData("0x0C41:0x0382")]
    [InlineData("0x0C41:0x04C3")]
    [InlineData("0x0C41:0xC226")]
    [InlineData("0x10DE1234")]
    [InlineData("0X0C41:0X0382")]   // upper-case 0X prefix
    [InlineData("0x0C41 - 0x0382")] // separator with spaces
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsUnnamedProfileName_TrueForRawIdentifiers(string? name)
    {
        Assert.True(ProfileIndexStore.IsUnnamedProfileName(name));
    }

    [Theory]
    // Real game titles from the same screenshot, plus titles engineered to look hex-adjacent.
    [InlineData("RF Online Next")]
    [InlineData("'83")]
    [InlineData("007 First Light")]
    [InlineData("007: Quantum of Solace")]
    [InlineData("3DMark")]
    [InlineData("0AD")]
    [InlineData("Half-Life 2")]
    [InlineData("S.T.A.L.K.E.R.")]
    [InlineData("0x0C41 Racing")]           // starts hex-like but is a title
    [InlineData("0xDEADBEEF Studios Game")] // ditto
    public void IsUnnamedProfileName_FalseForRealTitles(string name)
    {
        Assert.False(ProfileIndexStore.IsUnnamedProfileName(name));
    }

    [Fact]
    public void UnnamedProfileFilter_KeepsTitlesAndCountsTheRest()
    {
        // Mirrors what the dashboard does: partition the index into titles + a disclosure count.
        var indexed = new[]
        {
            "007 First Light", "0x0C41:0x0382", "RF Online Next",
            "0x0C41:0x04C3", "0x0C41:0xC226", "3DMark"
        };

        var named = indexed.Where(n => !ProfileIndexStore.IsUnnamedProfileName(n)).ToList();
        var unnamed = indexed.Length - named.Count;

        Assert.Equal(3, named.Count);
        Assert.Equal(3, unnamed);
        Assert.Contains("3DMark", named);
        Assert.DoesNotContain("0x0C41:0x0382", named);
    }

    // --- 2. Streamline column vocabulary ----------------------------------------------------

    [Fact]
    public void NotApplicableStreamline_UsesOneVocabularyAcrossScanners()
    {
        // Both scanners describe the same fact — Streamline has no version inside an NGX/AnWave
        // folder. NgxScanner has always said "N/A"; GlobalScanner (AnWave row) said "Unknown".
        // This pins the agreed word so a future edit to one can't silently diverge again.
        var ngxScannerSource = File.ReadAllText(FindSource("NgxScanner.cs"));
        var globalScannerSource = File.ReadAllText(FindSource("GlobalScanner.cs"));

        Assert.Contains("Streamline = \"N/A\"", ngxScannerSource);
        Assert.Contains("Streamline = \"N/A\"", globalScannerSource);
        Assert.DoesNotContain("Streamline = \"Unknown\"", globalScannerSource);

        // The StreamlineSDK row is deliberately DIFFERENT: there a Streamline version IS
        // applicable and merely undetermined, so "Unknown" is the correct word. N/A vs Unknown
        // is a real semantic distinction — this asserts it is preserved, not flattened.
        Assert.Contains("Streamline = \"Unknown\"", File.ReadAllText(FindSource("StreamlineScanner.cs")));
    }

    // --- 3. Version readers all route through DllVersionReader -------------------------------

    [Fact]
    public void DllVersionConsumers_DoNotReadFileVersionInfoDirectly()
    {
        // DllVersionReader is the single source of truth for "what version is this DLL" and is
        // the only place that normalizes comma-form resources ("310,7,0,0"). AnWaveAutoService
        // read FileVersionInfo directly and shipped "310.7,0,0" to the status card; three other
        // services had their own duplicate .Replace(',', '.') copies of the same logic.
        foreach (var file in new[]
                 {
                     "AnWaveAutoService.cs", "GlobalScanner.cs",
                     "StreamlineScanner.cs", "UpgradeService.cs"
                 })
        {
            var source = File.ReadAllText(FindSource(file));
            Assert.DoesNotContain("FileVersionInfo.GetVersionInfo", source);
        }

        // The reader itself must still make the call — that's where it belongs.
        Assert.Contains("FileVersionInfo.GetVersionInfo", File.ReadAllText(FindSource("DllVersionReader.cs")));
    }

    /// <summary>Walks up from the test assembly to locate a Core service source file.</summary>
    private static string FindSource(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "DLSSVersionToolkit.Core", "Services", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"Could not locate {fileName} from {AppContext.BaseDirectory}");
    }
}
