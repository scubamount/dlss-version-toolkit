using DLSSVersionToolkit.Core.Models;
using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Pins the version-gated preset rules (v0.0.52).
///
/// The DLSS 4.5 / Preset F row exists because of first-party testing on Windows: Ray
/// Reconstruction 310.7.128 only engages on Preset F, Preset E does not work. The boundary is the
/// whole point of the rule, so it is tested at, just below, and just above the exact version — two
/// earlier implementations of this predicate were wrong in exactly those places.
/// </summary>
public class PresetVersionRuleTests
{
    [Theory]
    // Below the gate — must NOT recommend F.
    [InlineData("310.7.0.0", false)]
    [InlineData("310.7.127.0", false)]
    [InlineData("310.7.127.99", false)]
    [InlineData("310.6.999.0", false)]
    // AT the gate. This is the build that was actually tested, in both the 3-part form the rule
    // is written in and the 4-part form FileVersionInfo returns. A string-equality implementation
    // passed the first and FAILED the second.
    [InlineData("310.7.128", true)]
    [InlineData("310.7.128.0", true)]
    // Above the gate.
    [InlineData("310.7.129.0", true)]
    [InlineData("310.8.0.0", true)]
    [InlineData("311.0.0.0", true)]
    // Numeric, not lexical: 310.10 > 310.7 even though "310.10" sorts before "310.7" as text.
    [InlineData("310.10.0.0", true)]
    public void RayReconstruction_RecommendsPresetF_AtOrAboveThreshold(string version, bool expectRule)
    {
        var rule = DlssPresetDisplay.FindPresetRule("nvngx_dlssd.dll", version);

        if (expectRule)
        {
            Assert.NotNull(rule);
            Assert.Equal(DlssPreset.F, rule!.Value.Preset);
        }
        else
        {
            Assert.Null(rule);
        }
    }

    [Theory]
    // An unreadable version must never drive a preset change. An implementation written as
    // !IsNewer(min, version) returned TRUE for every one of these, because an unparseable
    // comparison is false in both directions.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Unknown")]
    [InlineData("N/A")]
    [InlineData("garbage")]
    [InlineData("not.a.version")]
    public void UnreadableVersion_NeverRecommendsAPreset(string? version)
    {
        Assert.Null(DlssPresetDisplay.FindPresetRule("nvngx_dlssd.dll", version));
    }

    [Fact]
    public void CommaSeparatedVersion_IsAccepted()
    {
        // FileVersionInfo can hand back "310.7,128,0" in some locales; DllVersionReader normalizes
        // it, and this predicate must agree rather than silently declining to recommend.
        var rule = DlssPresetDisplay.FindPresetRule("nvngx_dlssd.dll", "310.7,128,0");
        Assert.NotNull(rule);
        Assert.Equal(DlssPreset.F, rule!.Value.Preset);
    }

    [Fact]
    public void RuleIsScopedToItsComponent()
    {
        // The RR rule must not fire for a different DLL that happens to be new enough.
        Assert.Null(DlssPresetDisplay.FindPresetRule("nvngx_dlss.dll", "310.7.128.0"));
        Assert.Null(DlssPresetDisplay.FindPresetRule("nvngx_dlssg.dll", "310.7.128.0"));
        Assert.Null(DlssPresetDisplay.FindPresetRule("nvngx_deepdvc.dll", "310.7.128.0"));
    }

    [Fact]
    public void UnknownComponent_ReturnsNull()
    {
        Assert.Null(DlssPresetDisplay.FindPresetRule("nvngx_nonexistent.dll", "999.0.0.0"));
        Assert.Null(DlssPresetDisplay.FindPresetRule(null, "310.7.128.0"));
        Assert.Null(DlssPresetDisplay.FindPresetRule("", "310.7.128.0"));
    }

    [Fact]
    public void EveryRuleTargetsAKnownNgxDll()
    {
        // A rule naming a DLL the app never installs is dead config that would never fire.
        foreach (var rule in DlssPresetDisplay.PresetVersionRules)
        {
            Assert.Contains(rule.DllName, UpgradeService.NgxDllNames);
            Assert.False(string.IsNullOrWhiteSpace(rule.Reason));
            Assert.False(string.IsNullOrWhiteSpace(rule.MinVersion));
        }
    }

    [Fact]
    public void PresetF_IsTheDocumentedValue()
    {
        // Preset F = 6. If this enum value ever drifts, the recommendation silently applies the
        // wrong preset while still reading as "F" in the UI.
        Assert.Equal(0x6, (int)DlssPreset.F);
    }
}
