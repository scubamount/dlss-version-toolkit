using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Regression tests for the v0.0.43 audit findings. Each test pins a specific bug so it
/// cannot silently return:
///  1. DeepDVC missing from the NGX sync DLL set (stayed stale forever while the other
///     three components updated).
///  2. DLSS cache restart-amnesia (session-only _cachedDownloadPath — the same bug class
///     fixed for Streamline in v0.0.40).
///  3. Lexical version ordering ("310.6.0" sorted above "310.10.0").
/// </summary>
public class AuditRegressionTests
{
    [Fact]
    public void NgxDllNames_CoversAllFourComponents()
    {
        // The INSTALLED VERSIONS grid tracks DLSS/FrameGen/DLSSD/DeepDVC — the sync set
        // must cover the same four components or one silently never updates.
        Assert.Equal(4, UpgradeService.NgxDllNames.Length);
        Assert.Contains("nvngx_dlss.dll", UpgradeService.NgxDllNames);
        Assert.Contains("nvngx_dlssg.dll", UpgradeService.NgxDllNames);
        Assert.Contains("nvngx_dlssd.dll", UpgradeService.NgxDllNames);
        Assert.Contains("nvngx_deepdvc.dll", UpgradeService.NgxDllNames);
    }

    [Theory]
    [InlineData("dlss-sdk-310.7.0.zip", "310.7.0")]
    [InlineData("dlss-sdk-310.10.0.zip", "310.10.0")]
    [InlineData("DLSS-SDK-310.7.0.ZIP", "310.7.0")] // case-insensitive
    [InlineData("streamline-sdk-2.12.0.zip", null)] // wrong prefix
    [InlineData("dlss-sdk-310.7.0.txt", null)]      // wrong suffix
    [InlineData("random.zip", null)]
    public void DlssParseVersionFromZipName_ParsesCacheNamesOnly(string fileName, string? expected)
    {
        Assert.Equal(expected, DlssDownloadService.ParseVersionFromZipName(fileName));
    }

    [Fact]
    public void VersionOrdering_IsNumericNotLexical()
    {
        // Lexical string ordering puts "310.6.0" above "310.10.0"; Version does not.
        var versions = new[] { "310.6.0", "310.10.0", "310.7.0" };
        var newest = versions
            .OrderByDescending(v => Version.TryParse(v, out var p) ? p : new Version(0, 0))
            .First();
        Assert.Equal("310.10.0", newest);
    }

    [Fact]
    public void FindLatestDownloadableRelease_SkipsAssetlessRelease()
    {
        var assetlessLatest = new DlssRelease { Version = "310.12.0", DownloadUrl = "" };
        var downloadableRelease = new DlssRelease
        {
            Version = "310.11.0",
            DownloadUrl = "https://example.test/ngx_dlss_demo_windows.zip"
        };

        var result = DlssDownloadService.FindLatestDownloadableRelease(
            new[] { assetlessLatest, downloadableRelease });

        Assert.Same(downloadableRelease, result);
    }

    [Fact]
    public void OrderCachedZipPathsNewestFirst_UsesSdkVersionNotFileTimestamp()
    {
        var paths = new[]
        {
            Path.Combine("cache", "dlss-sdk-310.6.0.zip"),
            Path.Combine("cache", "dlss-sdk-310.10.0.zip"),
            Path.Combine("cache", "dlss-sdk-310.7.0.zip")
        };

        var orderedNames = DlssDownloadService.OrderCachedZipPathsNewestFirst(paths)
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(
            new[] { "dlss-sdk-310.10.0.zip", "dlss-sdk-310.7.0.zip", "dlss-sdk-310.6.0.zip" },
            orderedNames);
    }

    [Fact]
    public void StreamlineCacheOrdering_UsesSdkVersionNotFileTimestamp()
    {
        var paths = new[]
        {
            Path.Combine("cache", "streamline-sdk-2.9.0.zip"),
            Path.Combine("cache", "streamline-sdk-2.10.0.zip"),
            Path.Combine("cache", "streamline-sdk-2.8.0.zip")
        };

        var orderedNames = StreamlineDownloadService.OrderCachedZipPathsNewestFirst(paths)
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(
            new[] { "streamline-sdk-2.10.0.zip", "streamline-sdk-2.9.0.zip", "streamline-sdk-2.8.0.zip" },
            orderedNames);
    }

    [Theory]
    [InlineData("nvidiaDlssGlom-v2.1.0-win64.rar", "2.1.0")]
    [InlineData("NVIDIADLSSGLOM-V2.10.0-win64.RAR", "2.10.0")]
    [InlineData("nvidiaDlssGlom-latest.rar", null)]
    [InlineData("unrelated.rar", null)]
    public void ParseGlomVersionFromArchiveName_ParsesReleaseNamesOnly(string fileName, string? expected)
    {
        Assert.Equal(expected, AnWaveAutoService.ParseGlomVersionFromArchiveName(fileName));
    }

    [Fact]
    public void OrderGlomArchivePathsNewestFirst_UsesReleaseVersionNotFileTimestamp()
    {
        var paths = new[]
        {
            Path.Combine("cache", "nvidiaDlssGlom-v2.9.0-win64.rar"),
            Path.Combine("cache", "nvidiaDlssGlom-v2.10.0-win64.rar"),
            Path.Combine("cache", "nvidiaDlssGlom-v2.8.0-win64.rar")
        };

        var orderedNames = AnWaveAutoService.OrderGlomArchivePathsNewestFirst(paths)
            .Select(Path.GetFileName)
            .ToArray();

        Assert.Equal(
            new[]
            {
                "nvidiaDlssGlom-v2.10.0-win64.rar",
                "nvidiaDlssGlom-v2.9.0-win64.rar",
                "nvidiaDlssGlom-v2.8.0-win64.rar"
            },
            orderedNames);
    }
}