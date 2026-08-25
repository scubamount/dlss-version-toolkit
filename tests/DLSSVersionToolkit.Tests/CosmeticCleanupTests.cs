using System.Text.RegularExpressions;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.0.59 cosmetic-cleanup gates. Both fixes close findings from the UI audit that v0.0.58
/// explicitly deferred and named, so they cannot be rediscovered as new:
///   * U10 — native MessageBox renders OS chrome (bright white in Windows light theme) mid-flow
///     over an all-dark app; every call site now routes through Views.ThemedMessageBox.
///   * U6  — disabled states used opacity collapse that blended below the 3:1 floor
///     (nav buttons measured 2.51:1 effective); opacities raised to clear it.
/// </summary>
public class CosmeticCleanupTests
{
    private static string Read(params string[] parts)
    {
        var srcRoot = SiblingSweepTests.FindRepoSubdir("src");
        return File.ReadAllText(Path.Combine(new[] { srcRoot }.Concat(parts).ToArray()));
    }

    [Fact]
    public void NoNativeMessageBoxCallSites_Remain()
    {
        var appDir = Path.Combine(SiblingSweepTests.FindRepoSubdir("src"), "DLSSVersionToolkit");
        var offenders = Directory.GetFiles(appDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("ThemedMessageBox.xaml.cs", StringComparison.Ordinal))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"(?<![.\w])MessageBox\.Show\(|System\.Windows\.MessageBox\.Show\("))
            .ToList();

        Assert.Empty(offenders);
    }

    /// <summary>The themed dialog must exist and actually use the app's own button styles.</summary>
    [Fact]
    public void ThemedMessageBox_UsesAppButtonStyles()
    {
        var cs = Read("DLSSVersionToolkit", "Views", "ThemedMessageBox.xaml.cs");

        Assert.Contains("PrimaryButtonStyle", cs);
        Assert.Contains("DarkButtonStyle", cs);
        Assert.Contains("ShowDialog()", cs);
    }

    /// <summary>Disabled opacity must keep effective contrast >= 3:1 against its surface.
    /// These are the raised values; anything lower re-blends into the panel.</summary>
    [Fact]
    public void DisabledStates_ClearContrastFloor()
    {
        var app = Read("DLSSVersionToolkit", "App.xaml");

        var nav = Regex.Matches(app, @"Opacity"" Value=""([\d.]+)""")
            .Select(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.NotEmpty(nav);
        // No disabled state may sit at the old illegible values (<= 0.5 on these surfaces).
        Assert.All(nav, v => Assert.True(v >= 0.55,
            $"disabled opacity {v} blends below the 3:1 legibility floor on its surface"));
        Assert.DoesNotContain(0.45, nav);
        Assert.DoesNotContain(0.4, nav);
    }
}
