using DLSSVersionToolkit.Core.Models;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.70 — the DLSS-RR default is Preset F.
///
/// The app carried two contradictory statements about the same question. PresetVersionRules has
/// said since it was written that Ray Reconstruction 310.7.128+ "needs Preset F — Preset E does
/// not engage the new model" (first-party observation on Andrew's machine). Meanwhile the
/// constant every fresh install and every Reset read from said E. Since every RR build this
/// toolkit installs is 310.7.128 or newer, the default was a preset that silently does nothing.
/// </summary>
public class PresetDefaultTests
{
    [Fact]
    public void RayReconstructionDefault_IsPresetF()
        => Assert.Equal(DlssPreset.F, DlssPresetDisplay.RayReconstructionDefault);

    /// <summary>
    /// The structural form: whatever the version rule requires for current RR builds is what a
    /// fresh install must start on. Change one without the other and this fails.
    /// </summary>
    [Fact]
    public void RayReconstructionDefault_AgreesWithTheVersionRule()
    {
        var rule = DlssPresetDisplay.PresetVersionRules
            .Single(r => r.DllName == "nvngx_dlssd.dll");

        Assert.Equal(rule.Preset, DlssPresetDisplay.RayReconstructionDefault);
    }

    /// <summary>
    /// The DEFAULT and RESET paths must read the constant, so changing the default stays one
    /// edit. A literal DlssPreset.E there would make the Reset button hand back the preset that
    /// does nothing.
    ///
    /// Scoped to those two paths deliberately. Other assignments are legitimate and must keep
    /// working: restoring a saved selection from settings (`= rr`), and applying a version rule
    /// the user accepted (`= rule.Value.Preset`). A gate that forbade those would be asserting
    /// that the app may never change the preset at all.
    /// </summary>
    [Fact]
    public void DefaultAndReset_ReadTheConstant_NotALiteral()
    {
        var src = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit", "ViewModels", "MainViewModel.cs"));

        // Field initializer (fresh install) and ResetSelectionsAsync (the Reset button).
        var fieldInit = src.Split('\n')
            .Single(l => l.Contains("_selectedRrPreset =") && !l.TrimStart().StartsWith("//"));
        Assert.Contains("RayReconstructionDefault", fieldInit);

        var resetStart = src.IndexOf("ResetSelectionsAsync()", StringComparison.Ordinal);
        Assert.True(resetStart > 0, "the Reset command must exist");
        var resetBody = src.Substring(resetStart, 1200);
        var resetLine = resetBody.Split('\n')
            .Single(l => l.Contains("SelectedRrPreset ="));
        Assert.Contains("RayReconstructionDefault", resetLine);
    }

    /// <summary>
    /// The doc comment on RayReconstructionPresets advertised "Recommended default: E" while the
    /// rule table said F. Prose drifts silently; pin it.
    /// </summary>
    [Fact]
    public void PresetDocs_DoNotStillRecommendE()
    {
        var models = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit.Core", "Models", "DlssPreset.cs"));

        Assert.DoesNotContain("Recommended default: E", models);
    }

    /// <summary>
    /// The other two defaults are unchanged — this fix is scoped to RR, and a preset overhaul
    /// that quietly moved SR or FG would be a different (unrequested) change.
    /// </summary>
    [Fact]
    public void OtherDefaults_AreUnchanged()
    {
        Assert.Equal(DlssPreset.L, DlssPresetDisplay.SuperResolutionDefault);
        Assert.Equal(DlssPreset.B, DlssPresetDisplay.FrameGenerationDefault);
    }
}
