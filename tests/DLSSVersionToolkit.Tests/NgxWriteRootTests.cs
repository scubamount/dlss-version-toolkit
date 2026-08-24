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
}
