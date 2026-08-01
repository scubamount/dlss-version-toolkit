using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Regression tests for the v0.0.44 whitelist rewrite. The bug: our fingerprint.db path used
/// XDocument.Parse, which threw on any file that is not strict XML and changed NOTHING — the
/// failure was reported as "XML parse error" at Debug level while the UI still said applied.
/// The reference script (JPersson77/nVAppAppApp.ps1) does plain text replacement, which works
/// regardless of the file's exact shape. These tests pin the text-replacement semantics.
/// </summary>
public class WhitelistFingerprintDbTests
{
    [Fact]
    public void FingerprintDb_FlipsAllFiveFlags()
    {
        var content =
            "<Root><Disable_FG_Override>1</Disable_FG_Override>" +
            "<Disable_RR_Override>1</Disable_RR_Override>" +
            "<Disable_SR_Override>1</Disable_SR_Override>" +
            "<Disable_RR_Model_Override>1</Disable_RR_Model_Override>" +
            "<Disable_SR_Model_Override>1</Disable_SR_Model_Override></Root>";

        var result = WhitelistService.FlipFingerprintDbFlags(content, out int flipped);

        Assert.Equal(5, flipped);
        Assert.DoesNotContain(">1<", result);
        Assert.Contains("<Disable_SR_Model_Override>0</Disable_SR_Model_Override>", result);
    }

    [Fact]
    public void FingerprintDb_NonXmlContent_StillFlips()
    {
        // The exact case the old XDocument.Parse path silently failed on: no root element,
        // stray bytes, unescaped junk. Text replacement does not care.
        var content = "\0\u0001binaryjunk<Disable_SR_Override>1</Disable_SR_Override>trailing & garbage";

        var result = WhitelistService.FlipFingerprintDbFlags(content, out int flipped);

        Assert.Equal(1, flipped);
        Assert.Contains("<Disable_SR_Override>0</Disable_SR_Override>", result);
    }

    [Fact]
    public void FingerprintDb_ToleratesAttributesAndWhitespace()
    {
        var content = "<Disable_FG_Override type=\"bool\"> 1 </Disable_FG_Override>";

        var result = WhitelistService.FlipFingerprintDbFlags(content, out int flipped);

        Assert.Equal(1, flipped);
        Assert.Contains("0", result);
        Assert.DoesNotContain(" 1 ", result);
    }

    [Fact]
    public void FingerprintDb_AlreadyZero_IsIdempotent()
    {
        var content = "<Disable_SR_Override>0</Disable_SR_Override>";

        var result = WhitelistService.FlipFingerprintDbFlags(content, out int flipped);

        Assert.Equal(0, flipped);
        Assert.Equal(content, result);
    }

    [Fact]
    public void FingerprintDb_DoesNotTouchUnrelatedOnes()
    {
        // A "1" elsewhere in the document must survive — we edit only the five gating flags.
        var content = "<SomeOtherSetting>1</SomeOtherSetting><Disable_FG_Override>1</Disable_FG_Override>";

        var result = WhitelistService.FlipFingerprintDbFlags(content, out int flipped);

        Assert.Equal(1, flipped);
        Assert.Contains("<SomeOtherSetting>1</SomeOtherSetting>", result);
        Assert.Contains("<Disable_FG_Override>0</Disable_FG_Override>", result);
    }
}
