using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Pins the read-path / write-path split in <see cref="NgxPathResolver"/> (v0.0.53).
///
/// The bug these exist for: <c>GetCandidatePaths</c> is led by the driver's registry-declared NGX
/// path, which on a real machine is
/// <c>C:\WINDOWS\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_*</c>. That is correct for
/// READING (the scanner just finds no models there and moves on) and catastrophic for WRITING: the
/// DriverStore is owned by TrustedInstaller and denies writes to Administrators by design, so
/// v0.0.52's Import Local DLLs failed for every single DLL with "could not create ...".
///
/// These tests use the explicit-roots overload so they prove the PREDICATE on synthetic Windows
/// paths. CI runs on windows-latest but the real WriteRoots are machine-specific, and a test that
/// asserted against live folders would pass vacuously on a runner with no NVIDIA driver.
/// </summary>
public class NgxWriteRootTests
{
    // The exact path from the v0.0.52 failure report.
    private const string DriverStore =
        @"C:\WINDOWS\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_0373d825005116d0";

    private static readonly string[] Roots =
    {
        @"C:\ProgramData\NVIDIA\NGX",
        @"C:\Users\andrew\AppData\Roaming\NVIDIA\NGX"
    };

    [Theory]
    // The regression itself: the driver store is never a write target, at any depth.
    [InlineData(DriverStore, false)]
    [InlineData(DriverStore + @"\Staging\models\dlss\versions\20318080\files", false)]
    // The two legitimate roots, and descendants of them.
    [InlineData(@"C:\ProgramData\NVIDIA\NGX", true)]
    [InlineData(@"C:\ProgramData\NVIDIA\NGX\Staging\models\dlss", true)]
    [InlineData(@"C:\Users\andrew\AppData\Roaming\NVIDIA\NGX", true)]
    // Containment must be separator-aware, not a bare StartsWith.
    [InlineData(@"C:\ProgramData\NVIDIA\NGX-evil", false)]
    // A parent of a root is not inside it.
    [InlineData(@"C:\ProgramData\NVIDIA", false)]
    [InlineData(@"C:\Windows\System32", false)]
    // Windows paths are case-insensitive.
    [InlineData(@"c:\programdata\nvidia\ngx\models", true)]
    // Degenerate input must be refused, never defaulted to "allowed".
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void IsWritableRoot_AcceptsOnlyNgxModelRoots(string? path, bool expected)
    {
        Assert.Equal(expected, NgxPathResolver.IsWritableRoot(path, Roots));
    }

    /// <summary>
    /// RED ARM. The old code took the first candidate that existed on disk, and on Andrew's machine
    /// that is the driver store. This asserts the driver store would have been chosen by that rule
    /// — so if someone reintroduces "just take candidate #1", this test fails and names why.
    /// </summary>
    [Fact]
    public void DriverStorePath_WouldHaveBeenChosenByFirstExistingCandidate_ButIsNotWritable()
    {
        var candidateOrder = new[] { DriverStore, @"C:\ProgramData\NVIDIA\NGX" };

        // The discarded rule: first candidate wins.
        var oldPick = candidateOrder[0];
        Assert.Equal(DriverStore, oldPick);
        Assert.False(NgxPathResolver.IsWritableRoot(oldPick, Roots),
            "the driver store must never be accepted as a write target");

        // The rule that replaced it: filter to write roots first.
        var newPick = candidateOrder.FirstOrDefault(p => NgxPathResolver.IsWritableRoot(p, Roots));
        Assert.Equal(@"C:\ProgramData\NVIDIA\NGX", newPick);
    }

    /// <summary>
    /// The real WriteRoots must never contain a system directory, on any machine CI runs on.
    /// This is the machine-independent half: it asserts a property of the list, not its contents.
    /// </summary>
    [Fact]
    public void WriteRoots_AreUnderUserOrProgramData_NeverSystem32()
    {
        Assert.NotEmpty(NgxPathResolver.WriteRoots);

        foreach (var root in NgxPathResolver.WriteRoots)
        {
            Assert.DoesNotContain("System32", root, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DriverStore", root, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(Path.Combine("NVIDIA", "NGX"), root, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// GetWritableBase must never hand back something IsWritableRoot rejects. Any disagreement
    /// between the resolver and its own guard is the v0.0.52 failure mode returning.
    /// </summary>
    [Fact]
    public void GetWritableBase_ReturnsSomethingItsOwnGuardAccepts()
    {
        var basePath = NgxPathResolver.GetWritableBase(null);

        // May be null only if the machine has neither ProgramData nor AppData — never on Windows.
        if (basePath is not null)
            Assert.True(NgxPathResolver.IsWritableRoot(basePath),
                $"GetWritableBase returned {basePath}, which its own guard rejects");
    }

    /// <summary>
    /// An explicit user-configured path pointing at the driver store must be ignored, not honored.
    /// Settings is not an escape hatch out of the write allowlist.
    /// </summary>
    [Fact]
    public void GetWritableBase_IgnoresExplicitPathOutsideWriteRoots()
    {
        var basePath = NgxPathResolver.GetWritableBase(DriverStore);

        Assert.NotEqual(DriverStore, basePath);
        if (basePath is not null)
            Assert.True(NgxPathResolver.IsWritableRoot(basePath));
    }

    /// <summary>
    /// One rule, one predicate. Before v0.0.53 the "%ProgramData%\NVIDIA\NGX" path was rebuilt
    /// inline in SEVEN places (UpgradeService x3, both download services, MainViewModel x2), each
    /// blind to an AppData-based tree or a configured path. This asserts the literal pair appears
    /// only inside NgxPathResolver, where WriteRoots and GetCandidatePaths define it once.
    ///
    /// v0.0.57 hardening: the check is now PER LINE, not per file. The original per-file skip for
    /// "any file mentioning GetWritableBase(null)" let a second, unrelated literal in the same
    /// file ride in behind the sanctioned fallback (verdict unit narrower than enforcement unit —
    /// the detector-integrity failure mode). Now each line carrying the literal must BE the
    /// sanctioned `?? Path.Combine(...)` fallback itself; no other exemption exists.
    /// </summary>
    [Fact]
    public void NgxRootLiteral_IsDefinedOnlyInTheResolver()
    {
        var srcRoot = FindRepoSubdir("src");
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name == "NgxPathResolver.cs")
                continue;

            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!line.Contains("\"NVIDIA\", \"NGX\"", StringComparison.Ordinal))
                    continue;

                // The ONE sanctioned form: the null-coalescing default behind GetWritableBase.
                // Checked over a small statement window because the fallback legitimately wraps
                // across three lines (`= X.GetWritableBase(null)` / `?? Path.Combine(` /
                // `"NVIDIA", "NGX");`). A whole-file exemption was the v0.0.53-56 hole; ±4 lines
                // keeps the verdict local to the statement without false-positive wrapping.
                var windowStart = Math.Max(0, i - 4);
                var windowEnd = Math.Min(lines.Length - 1, i + 2);
                var window = string.Join("\n", lines[windowStart..(windowEnd + 1)]);
                var isSanctionedFallback =
                    window.Contains("GetWritableBase(null)", StringComparison.Ordinal) &&
                    window.Contains("?? Path.Combine", StringComparison.Ordinal);

                if (!isSanctionedFallback)
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}");
            }
        }

        Assert.True(offenders.Count == 0,
            "NGX root path rebuilt outside NgxPathResolver at: " + string.Join(", ", offenders) +
            ". Use NgxPathResolver.GetWritableBase for writes, GetCandidatePaths for reads, " +
            "GetConfigFilePath() for nvngx_config.txt.");
    }

    /// <summary>
    /// The OTA cache probe, added in v0.74 after a grep showed <c>OTACachePath</c> appeared
    /// nowhere in <c>src/</c>.
    ///
    /// NVIDIA's own resolver (Streamline <c>source/core/sl.ota/ota.cpp</c>, <c>OTA::getNGXPath</c>)
    /// reads <c>HKLM\SOFTWARE\NVIDIA Corporation\Global\NGXCore : OTACachePath</c> and returns
    /// immediately when it is set, BEFORE falling back to <c>%ProgramData%\NVIDIA\NGX</c>. We
    /// probed four other values in that key and not this one, so on a machine where the driver
    /// relocated its OTA cache the scanner looked at a path the driver no longer populates and
    /// truthfully reported nothing — a silent miss, which is the failure mode a probe list has.
    ///
    /// Asserting the SET rather than the lookup is the point: an omitted probe cannot throw, so
    /// only an explicit membership check can fail when one goes missing again.
    /// </summary>
    [Fact]
    public void RegistryProbes_IncludeOtaCachePath_AheadOfProgramDataFallback()
    {
        var probes = NgxPathResolver.RegistryProbes;

        Assert.Contains(
            (@"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "OTACachePath"),
            probes);

        // Every value NVIDIA's chain touches, so a future edit cannot drop one silently.
        foreach (var expected in new[] { "OTACachePath", "NGXPath", "FullPath" })
        {
            Assert.Contains(probes,
                p => p.Key == @"SOFTWARE\NVIDIA Corporation\Global\NGXCore" && p.Value == expected);
        }

        Assert.Equal(probes.Distinct().Count(), probes.Count);
    }

    /// <summary>
    /// The resolver must never hand a caller a duplicate or blank candidate, and must not throw on
    /// a machine with no NVIDIA driver (every CI runner). Cheap, but this is the path that feeds
    /// every scan.
    /// </summary>
    [Fact]
    public void GetCandidatePaths_IsDeduplicated_AndNeverBlank()
    {
        var candidates = NgxPathResolver.GetCandidatePaths(@"C:\ProgramData\NVIDIA\NGX");

        Assert.NotEmpty(candidates);
        Assert.DoesNotContain(candidates, string.IsNullOrWhiteSpace);
        Assert.Equal(
            candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            candidates.Count);
        Assert.Equal(@"C:\ProgramData\NVIDIA\NGX", candidates[0]);
    }

    /// <summary>
    /// Walks up from the test binary to the repo root and returns the named subdirectory.
    /// CI runs from bin/Release/net9.0, so this needs several hops.
    /// </summary>
    private static string FindRepoSubdir(string name)
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
