using System.Text.RegularExpressions;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.0.58 UI accessibility/polish gates, from the static XAML audit (contrast ratios computed
/// independently and confirmed: #757575 on #171717 = 3.89:1). These pin SOURCE SHAPE — macOS has
/// no WPF runtime to render into, so CI cannot screenshot; what CI can do is fail the moment a
/// fix regresses (a focus style deleted, a fixed Height reintroduced). Each was red/green armed
/// against a git-archive tree of v0.0.57 before spending CI.
/// </summary>
public class UiAccessibilityTests
{
    private static string Read(params string[] parts)
    {
        var srcRoot = SiblingSweepTests.FindRepoSubdir("src");
        return File.ReadAllText(Path.Combine(new[] { srcRoot }.Concat(parts).ToArray()));
    }

    /// <summary>Keyboard users had zero visible focus anywhere in the app: all four custom button
    /// templates replaced the default chrome without an IsFocused cue.</summary>
    [Fact]
    public void ButtonStyles_DefineVisibleKeyboardFocus()
    {
        var app = Read("DLSSVersionToolkit", "App.xaml");

        Assert.Contains("x:Key=\"VisibleFocusStyle\"", app);
        foreach (var style in new[] { "NavButtonStyle", "PrimaryButtonStyle", "SolidGreenButtonStyle", "DarkButtonStyle" })
        {
            // each style block must carry the shared focus visual
            var idx = app.IndexOf($"x:Key=\"{style}\"", StringComparison.Ordinal);
            var next = app.IndexOf("<Style x:Key=", idx + 1, StringComparison.Ordinal);
            if (next < 0) next = app.Length;
            var block = app[idx..next];
            Assert.True(block.Contains("FocusVisualStyle"), $"style {style} lost its FocusVisualStyle");
        }
    }

    /// <summary>#757575 measured 3.89:1 on Panel2 (#171717), under the 4.5:1 AA floor for the
    /// 10-11px text it is used on. The token must stay at or above ~5:1 on every surface.</summary>
    [Fact]
    public void Text3Token_MeetsContrastFloor_OnEveryPanelSurface()
    {
        var m = Regex.Match(Read("DLSSVersionToolkit", "App.xaml"), @"Text3Color"">(#[0-9A-Fa-f]{6})");
        Assert.True(m.Success, "Text3Color token missing from App.xaml");
        Assert.True(Contrast(m.Groups[1].Value, "#171717") >= 4.5,
            $"Text3Color {m.Groups[1].Value} fell below 4.5:1 on Panel2");
        Assert.True(Contrast(m.Groups[1].Value, "#0E0E0E") >= 4.5,
            $"Text3Color {m.Groups[1].Value} fell below 4.5:1 on Panel1");
    }

    /// <summary>The v0.0.48 DPI-clip class: a fixed pixel Height clips dialog content behind the
    /// bottom edge at high scaling. Dialogs size by content inside a resizable frame.</summary>
    [Fact]
    public void Dialogs_SizeToContent_NeverFixedPixelHeight()
    {
        foreach (var dlg in new[] { "SettingsDialog.xaml", "BackupsDialog.xaml" })
        {
            var x = Read("DLSSVersionToolkit", "Views", dlg);
            Assert.Contains("SizeToContent=\"Height\"", x);
            Assert.DoesNotMatch(@"Height=""\d{3}""", x.Replace("MaxHeight", "").Replace("MinHeight", ""));
            Assert.Contains("CanResize", x);
        }
    }

    /// <summary>Status dots signalled state by fill color alone; screen readers announced
    /// nothing. AutomationProperties.Name must carry the state.</summary>
    [Fact]
    public void StatusDots_CarryAccessibleStateNames()
    {
        var main = Read("DLSSVersionToolkit", "MainWindow.xaml");

        Assert.Contains("AutomationProperties.Name=\"DLSS Indicator status dot\"", main);
        Assert.Contains("\"AnWave: Installed\"", main);
    }

    /// <summary>The 🔒 override cell's meaning lived only in a hover tooltip.</summary>
    [Fact]
    public void OverrideMarkerCell_AccessibleNameBoundToTooltip()
    {
        var main = Read("DLSSVersionToolkit", "MainWindow.xaml");

        var colAt = main.IndexOf("Header=\"Override\"", StringComparison.Ordinal);
        var nameAt = main.IndexOf(
            "AutomationProperties.Name\" Value=\"{Binding OverrideTooltip}\"", StringComparison.Ordinal);
        Assert.True(colAt >= 0 && nameAt > colAt && nameAt - colAt < 1200,
            "Override column lost its bound AutomationProperties.Name");
    }

    /// <summary>An error message that states a failure but not the next action blocks the user.
    /// Backups restore failures must say what to do.</summary>
    [Fact]
    public void BackupRestoreErrors_NextActionPresent()
    {
        var cs = Read("DLSSVersionToolkit", "Views", "BackupsDialog.xaml.cs");

        Assert.Matches(@"cancelled[\s\S]{0,200}What to do:", cs);
        Assert.Matches(@"Restore failed[\s\S]{0,200}What to do:", cs);
    }

    /// <summary>The default Expander template renders light chrome that vanishes on the
    /// true-black canvas; an implicit dark style must exist.</summary>
    [Fact]
    public void Expander_HasDarkImplicitTemplate()
    {
        Assert.Contains("<Style TargetType=\"Expander\">", Read("DLSSVersionToolkit", "App.xaml"));
    }

    /// <summary>"Already installed/applied" was communicated by Opacity 0.55 + hover-only
    /// tooltip. State must be text at full opacity.</summary>
    [Fact]
    public void AlreadyAppliedStates_AreTextNotDimming()
    {
        var main = Read("DLSSVersionToolkit", "MainWindow.xaml");

        Assert.Contains("(installed)", main);
        Assert.Contains("(applied)", main);
        Assert.DoesNotContain("Opacity\" Value=\"0.55\"", main);
    }

    /// <summary>WCAG relative-luminance contrast ratio, for gating color tokens in CI.</summary>
    internal static double Contrast(string hexA, string hexB)
    {
        double Lum(string h)
        {
            double Chan(int i)
            {
                var c = Convert.ToInt32(h.Substring(1 + i * 2, 2), 16) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Chan(0) + 0.7152 * Chan(1) + 0.0722 * Chan(2);
        }
        var la = Lum(hexA); var lb = Lum(hexB);
        var hi = Math.Max(la, lb); var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }
}
