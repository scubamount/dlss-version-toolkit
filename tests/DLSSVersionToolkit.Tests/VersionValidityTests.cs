using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Behavior gates for the ONE version-validity predicate (DllVersionReader.IsValidVersion).
/// v0.0.61 unified three private copies — and inherited their off-by-one: {1,3} means 2 to FIVE
/// dotted parts, so real 2-part versions ("310.6", config files, BuildIDs) read "Unknown" while
/// 5-part garbage passed. v0.0.62 fixes it to {0,2} = 2-4 parts. These are the truth-table
/// cases; the structural gate VersionValidityRegex_HasOneDefinition keeps the rule singular.
/// </summary>
public class VersionValidityTests
{
    [Theory]
    [InlineData("310.6", true)]        // 2-part: valid — this was the regression
    [InlineData("310.7.0", true)]      // 3-part
    [InlineData("310.7.0.0", true)]    // 4-part
    [InlineData("310", false)]         // no dot = not a version
    [InlineData("310.6.0.0.0", false)] // 5-part garbage (the shipped regex wrongly passed this)
    [InlineData("", false)]
    [InlineData("garbage", false)]
    [InlineData("310.", false)]        // trailing dot
    [InlineData("3xx.6", false)]       // letters
    public void IsValidVersion_TruthTable(string version, bool expected)
    {
        Assert.Equal(expected, DllVersionReader.IsValidVersion(version));
    }

    [Fact]
    public void IsValidVersion_Null_IsInvalid()
    {
        Assert.False(DllVersionReader.IsValidVersion(null));
    }

    [Fact]
    public void TwoPartVersion_ComparesAgainstFourPart()
    {
        // The consequence that made the bug user-visible: 2-part forms must compare, not
        // vanish into "Unknown". VersionComparer pads to 4 ("310.6" == "310.6.0.0").
        var cmp = new VersionComparer();
        Assert.True(cmp.IsNewer("310.7", "310.6.0.0"));
        Assert.False(cmp.IsNewer("310.6.0.0", "310.7"));
    }
}
