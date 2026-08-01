using DLSSVersionToolkit.Core.Services;
using Xunit;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Pins the v0.0.45 IsOpsSupported unlock. NVIDIA App gates its DLSS Override UI on
/// "IsOpsSupported": titles with no published Optimal Playable Settings ship false and
/// show "not supported" even when all five Disable_*_Override flags are already false.
///
/// The two properties that matter and could silently regress:
///   1. DIRECTION — false -> true. Flipping it the other way would hide the override UI.
///   2. CmsId GATE — entries with "CmsId":0 are user-added bare executables NVIDIA cannot
///      identify; asserting OPS support for them is meaningless, so they must be skipped.
/// </summary>
public class WhitelistIsOpsSupportedTests
{
    // Shape copied from a real ApplicationStorage.json: root object, "Applications" array,
    // each element { "LocalId":..., "Application": { ... } }. Compact, as NVIDIA writes it.
    private const string RealShapeJson = """
    {"Applications":[
    {"LocalId":21395317,"Application":{"CmsId":100876711,"CmsVersion":1,"DisplayName":"Working Game","ShortName":"working","Version":"steam","IsOpsSupported":true,"Disable_FG_Override":false,"Disable_SR_Override":false,"DLSS_Override_No_OPS":false}},
    {"LocalId":21395318,"Application":{"CmsId":101432111,"CmsVersion":1,"DisplayName":"Star Citizen","ShortName":"star_citizen","Version":"generic","IsOpsSupported":false,"Disable_FG_Override":false,"Disable_SR_Override":false,"DLSS_Override_No_OPS":false}},
    {"LocalId":21395319,"Application":{"CmsId":0,"CmsVersion":0,"DisplayName":"SomeGame.exe","ShortName":"","Version":"","IsOpsSupported":false,"Disable_FG_Override":false}}
    ]}
    """;

    [Fact]
    public void FlipIsOpsSupported_UnlocksIdentifiedGame_SkipsUnidentifiedExe()
    {
        var result = WhitelistService.FlipIsOpsSupported(RealShapeJson, out var flipped);

        // Only the CmsId-bearing entry that was false gets flipped.
        Assert.Single(flipped);
        Assert.Equal("Star Citizen", flipped[0]);

        // Direction: the target value is true, never the reverse.
        Assert.Contains("\"DisplayName\":\"Star Citizen\",\"ShortName\":\"star_citizen\",\"Version\":\"generic\",\"IsOpsSupported\":true", result);

        // CmsId:0 entry is left alone — still false.
        Assert.Contains("\"DisplayName\":\"SomeGame.exe\",\"ShortName\":\"\",\"Version\":\"\",\"IsOpsSupported\":false", result);

        // An already-true entry is untouched, and nothing was flipped backwards.
        Assert.Contains("\"DisplayName\":\"Working Game\",\"ShortName\":\"working\",\"Version\":\"steam\",\"IsOpsSupported\":true", result);
        Assert.DoesNotContain("\"IsOpsSupported\": false", result);

        // Only the one field changed: same length delta as "false"->"true" exactly once.
        Assert.Equal(RealShapeJson.Length - 1, result.Length);

        // The five Disable_* flags and DLSS_Override_No_OPS are NOT our business here.
        // DLSS_Override_No_OPS's correct target is false (per dlss-override-plus KEY_SPECS),
        // which is what the fixture already has — this method must not touch it.
        // 2 x DLSS_Override_No_OPS:false + 2 x Disable_SR_Override:false, all preserved.
        Assert.Equal(4, System.Text.RegularExpressions.Regex.Matches(result, "\"DLSS_Override_No_OPS\":false").Count
                        + System.Text.RegularExpressions.Regex.Matches(result, "\"Disable_SR_Override\":false").Count);
    }

    [Fact]
    public void CountUnlockableApps_AgreesWithFlip()
    {
        // Detector and applier must share one function so they can never disagree
        // (the v0.0.44 false "already applied" lesson).
        WhitelistService.FlipIsOpsSupported(RealShapeJson, out var flipped);
        Assert.Equal(flipped.Count, WhitelistService.CountUnlockableApps(RealShapeJson));

        // Idempotent: after flipping, there is nothing left to unlock.
        var once = WhitelistService.FlipIsOpsSupported(RealShapeJson, out _);
        Assert.Equal(0, WhitelistService.CountUnlockableApps(once));
    }

    [Fact]
    public void FlipIsOpsSupported_HandlesWhitespaceAndNoEntries()
    {
        // Pretty-printed variant must still match.
        const string spaced = """
        {"Applications":[{"LocalId":1,"Application":{"CmsId":123,"DisplayName":"Spaced","IsOpsSupported" : false}}]}
        """;
        var result = WhitelistService.FlipIsOpsSupported(spaced, out var flipped);
        Assert.Single(flipped);
        Assert.Contains("true", result);

        // No app entries at all: returned unchanged, nothing reported.
        const string empty = """{"Applications":[]}""";
        Assert.Equal(empty, WhitelistService.FlipIsOpsSupported(empty, out var none));
        Assert.Empty(none);
    }
}
