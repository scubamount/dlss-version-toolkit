using DLSSVersionToolkit.Core.Models;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Tests for the DLSSG (DLSS Frame Generation generator) mode + multiplier mapping added to
/// expose Fixed/Dynamic + the 2x/3x/4x… multiplier from the toolkit (previously only settable
/// via the NVIDIA App). The multiplier↔frame-count arithmetic is the bug-prone bit: the DRS
/// field stores GENERATED frames (count), while the UI shows the displayed MULTIPLIER (count+1).
/// </summary>
public class DlssgFrameGenTests
{
    // --- Setting IDs match NVIDIA's NvApiDriverSettings.h ---

    [Fact]
    public void DlssgSettingIds_MatchNvApiHeader()
    {
        Assert.Equal(0x10308298u, DlssPresetSettingIds.DLSSG_MODE);
        Assert.Equal(0x104D6667u, DlssPresetSettingIds.DLSSG_MULTI_FRAME_COUNT);
        Assert.Equal(0x10562D0Fu, DlssPresetSettingIds.DLSSG_DYNAMIC_MULTI_FRAME_COUNT_MAX);
        Assert.Equal(0x10CF4125u, DlssPresetSettingIds.DLSSG_DYNAMIC_TARGET_FRAME_RATE);
        Assert.Equal(0x01000000u, DlssPresetSettingIds.DLSSG_DYNAMIC_TARGET_FRAME_RATE_AUTO);
    }

    [Fact]
    public void DlssgMode_EnumValues_MatchNvApiHeader()
    {
        Assert.Equal(0u, (uint)DlssgMode.Disabled);
        Assert.Equal(1u, (uint)DlssgMode.Off);
        Assert.Equal(2u, (uint)DlssgMode.On);
        Assert.Equal(3u, (uint)DlssgMode.Auto);
        Assert.Equal(4u, (uint)DlssgMode.Dynamic);
    }

    // --- Multiplier <-> frame count arithmetic (the foot-gun) ---

    [Theory]
    [InlineData(2, 1u)]   // 2x => 1 generated frame
    [InlineData(3, 2u)]   // 3x => 2
    [InlineData(4, 3u)]   // 4x => 3
    [InlineData(5, 4u)]   // 5x => 4
    [InlineData(6, 5u)]   // 6x => 5 (the user's "6x" case)
    public void MultiplierToFrameCount_IsMultiplierMinusOne(int multiplier, uint expectedCount)
    {
        Assert.Equal(expectedCount, DlssPresetDisplay.MultiplierToFrameCount(multiplier));
    }

    [Theory]
    [InlineData(1u, 2)]
    [InlineData(5u, 6)]
    public void FrameCountToMultiplier_IsCountPlusOne(uint count, int expectedMultiplier)
    {
        Assert.Equal(expectedMultiplier, DlssPresetDisplay.FrameCountToMultiplier(count));
    }

    [Fact]
    public void MultiplierToFrameCount_ClampsBelowMinimum()
    {
        // 1x or 0x is not a valid FG multiplier; clamp to 2x => count 1.
        Assert.Equal(1u, DlssPresetDisplay.MultiplierToFrameCount(1));
        Assert.Equal(1u, DlssPresetDisplay.MultiplierToFrameCount(0));
    }

    [Fact]
    public void MultiplierToFrameCount_ClampsAboveMaximum()
    {
        // The count field tops out at 15 (=> 16x); anything higher clamps.
        Assert.Equal(15u, DlssPresetDisplay.MultiplierToFrameCount(16));
        Assert.Equal(15u, DlssPresetDisplay.MultiplierToFrameCount(99));
    }

    [Fact]
    public void RoundTrip_MultiplierToCountAndBack_IsStable()
    {
        foreach (var m in DlssPresetDisplay.FrameGenMultipliers)
        {
            var count = DlssPresetDisplay.MultiplierToFrameCount(m);
            Assert.Equal(m, DlssPresetDisplay.FrameCountToMultiplier(count));
        }
    }

    // --- Display lists + labels ---

    [Fact]
    public void FrameGenModes_ContainsAllUserSelectableModes()
    {
        Assert.Contains(DlssgMode.Dynamic, DlssPresetDisplay.FrameGenModes);
        Assert.Contains(DlssgMode.On, DlssPresetDisplay.FrameGenModes);
        Assert.Contains(DlssgMode.Off, DlssPresetDisplay.FrameGenModes);
        Assert.Contains(DlssgMode.Auto, DlssPresetDisplay.FrameGenModes);
        Assert.Contains(DlssgMode.Disabled, DlssPresetDisplay.FrameGenModes);
    }

    [Fact]
    public void Defaults_AreDynamic4xMatchRefresh()
    {
        Assert.Equal(DlssgMode.Dynamic, DlssPresetDisplay.FrameGenModeDefault);
        Assert.Equal(4, DlssPresetDisplay.FrameGenMultiplierDefault);
    }

    [Fact]
    public void GetModeLabel_FixedAndDynamicReadable()
    {
        Assert.Equal("Fixed", DlssPresetDisplay.GetModeLabel(DlssgMode.On));
        Assert.Equal("Dynamic", DlssPresetDisplay.GetModeLabel(DlssgMode.Dynamic));
        Assert.Equal("Don't change", DlssPresetDisplay.GetModeLabel(DlssgMode.Disabled));
    }

    [Fact]
    public void GetMultiplierLabel_FormatsAsNx()
    {
        Assert.Equal("4x", DlssPresetDisplay.GetMultiplierLabel(4));
        Assert.Equal("6x", DlssPresetDisplay.GetMultiplierLabel(6));
    }

    // --- PresetApplyOptions defaults preserve back-compat ---

    [Fact]
    public void PresetApplyOptions_DefaultMode_IsDisabled_ForBackCompat()
    {
        // Existing callers that don't set a mode must NOT change the DLSSG mode knob.
        var opts = new Core.Services.PresetApplyOptions();
        Assert.Equal(DlssgMode.Disabled, opts.FrameGenerationMode);
        Assert.Null(opts.FrameGenerationDynamicTargetFps);
    }
}
