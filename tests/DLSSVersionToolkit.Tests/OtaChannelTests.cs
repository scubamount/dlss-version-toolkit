using DLSSVersionToolkit.Core.Services;
using DLSSVersionToolkit.Core.Models;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.72 — staging-channel visibility and OTA payload download safety.
///
/// The download path pulls NVIDIA-copyrighted binaries from an undocumented endpoint and writes
/// them into the NGX tree. Every rejection arm below is armed with input that SHOULD be refused,
/// because a verifier that has only ever been tested on good input is an unproven verifier.
/// </summary>
public class OtaChannelTests
{
    // ---- channel selection -------------------------------------------------

    [Fact]
    public void StagingWins_OnlyWhenStrictlyNewer()
    {
        // Production and staging at the same version: production must own the answer, so the
        // user never sees "pre-release" on a build production also serves.
        Assert.Equal(0, NvidiaOtaService.CompareVersions("310.7.128", "310.7.128"));
        Assert.True(NvidiaOtaService.CompareVersions("310.9.0", "310.7.128") > 0);
    }

    [Fact]
    public void CompareVersions_IsNumeric_NotLexical()
    {
        // The live case that motivates this: 2.14.0 (staging) vs 2.12.128 (production).
        Assert.True(NvidiaOtaService.CompareVersions("2.14.0", "2.12.128") > 0);
        // And the trap: 310.7.128 is newer than 310.7.9 but sorts before it as a string.
        Assert.True(NvidiaOtaService.CompareVersions("310.7.128", "310.7.9") > 0);
    }

    [Fact]
    public void ParsedVersions_CarryTheirChannel()
    {
        const string manifest = "[dlss]\napp_E658700 = 310.9.0\n";

        var prod = NvidiaOtaService.Parse(manifest, OtaChannel.Production).Single();
        var staging = NvidiaOtaService.Parse(manifest, OtaChannel.Staging).Single();

        Assert.False(prod.IsPreRelease);
        Assert.True(staging.IsPreRelease);
    }

    [Fact]
    public void ChannelRoots_AreDistinct_AndProductionIsTheDefault()
    {
        Assert.Equal(NvidiaOtaService.ProductionChannel, NvidiaOtaService.RootFor(OtaChannel.Production));
        Assert.Equal(NvidiaOtaService.StagingChannel, NvidiaOtaService.RootFor(OtaChannel.Staging));
        Assert.NotEqual(NvidiaOtaService.ProductionChannel, NvidiaOtaService.StagingChannel);

        // v0.73: both update-source preferences ship on. The licensing acceptance does NOT —
        // a preference default must never stand in for consent (see IsDownloadPermitted).
        var settings = new DLSSVersionToolkit.Core.Models.AppSettings();
        Assert.True(settings.IncludePreReleaseChannel);
        Assert.True(settings.AllowOtaPayloadDownloads);
        Assert.False(settings.OtaRedistributionAccepted);
    }

    // ---- payload URL shape -------------------------------------------------

    [Fact]
    public void PackVersion_MatchesNgxLayout()
    {
        // Verified against a live 200: 310.7.128 -> 20318080.
        Assert.Equal(20318080, OtaPayloadDownloader.PackVersion("310.7.128"));
    }

    [Fact]
    public void PayloadUrl_IsHttps_AndChannelScoped()
    {
        var prod = OtaPayloadDownloader.BuildPayloadUrl(OtaChannel.Production, "dlss", "310.7.128");
        var staging = OtaPayloadDownloader.BuildPayloadUrl(OtaChannel.Staging, "dlss", "310.9.0");

        Assert.StartsWith("https://", prod);
        Assert.Contains(NvidiaOtaService.ProductionChannel, prod);
        Assert.Contains("/20318080/", prod);
        Assert.Contains(NvidiaOtaService.StagingChannel, staging);
        Assert.DoesNotContain(NvidiaOtaService.ProductionChannel, staging);
    }

    // ---- download rejection arms ------------------------------------------

    private sealed class StubAuthenticode : IAuthenticodeVerifier
    {
        private readonly bool _valid;
        public StubAuthenticode(bool valid) => _valid = valid;

        public AuthenticodeResult Verify(string filePath) => new()
        {
            IsValid = _valid,
            Detail = _valid ? "stub: accepted" : "stub: signer is not NVIDIA",
        };
    }

    private static HttpClient StubHttp(Func<HttpRequestMessage, HttpResponseMessage> handler) =>
        new(new DelegateHandler(handler));

    private sealed class DelegateHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;
        public DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }

    private static HttpResponseMessage Ok(byte[] body) =>
        new(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static HttpResponseMessage Ok(string body) =>
        new(System.Net.HttpStatusCode.OK) { Content = new StringContent(body) };

    private static string TempTarget() =>
        Path.Combine(Path.GetTempPath(), "dlssvt-ota-tests", Guid.NewGuid().ToString("N"), "nvngx_dlss.dll");

    /// <summary>
    /// Settings with both consent flags set. The verification tests below exercise digest/PE/
    /// signature behavior, which is only reachable once consent is granted, so they must pass
    /// this explicitly — see Download_Refuses_WithoutRedistributionAcceptance for the gate itself.
    /// </summary>
    private static AppSettings Permitting() => new()
    {
        AllowOtaPayloadDownloads = true,
        OtaRedistributionAccepted = true,
    };

    /// <summary>GREEN arm: correct digest, PE bytes, accepted signer.</summary>
    [Fact]
    public async Task Download_Succeeds_WhenDigestAndSignatureAreGood()
    {
        var payload = MakePeBytes();
        var digest = Sha256Hex(payload);

        var downloader = new OtaPayloadDownloader(
            StubHttp(req => req.RequestUri!.AbsoluteUri.EndsWith(".sha256") ? Ok(digest) : Ok(payload)),
            new StubAuthenticode(valid: true));

        var target = TempTarget();
        var result = await downloader.DownloadAsync("dlss", "310.7.128", target, OtaChannel.Production, Permitting());

        Assert.True(result.Success, result.Error);
        Assert.True(File.Exists(target));
        Assert.Equal(digest, result.Sha256);
    }

    /// <summary>RED arm: the bytes do not match the published digest.</summary>
    [Fact]
    public async Task Download_Refuses_OnDigestMismatch()
    {
        var payload = MakePeBytes();
        var wrongDigest = Sha256Hex(new byte[] { 1, 2, 3 });

        var downloader = new OtaPayloadDownloader(
            StubHttp(req => req.RequestUri!.AbsoluteUri.EndsWith(".sha256") ? Ok(wrongDigest) : Ok(payload)),
            new StubAuthenticode(valid: true));

        var target = TempTarget();
        var result = await downloader.DownloadAsync("dlss", "310.7.128", target, OtaChannel.Production, Permitting());

        Assert.False(result.Success);
        Assert.Contains("SHA-256 mismatch", result.Error);
        Assert.False(File.Exists(target));
    }

    /// <summary>RED arm: no sidecar means nothing to verify against — must not install.</summary>
    [Fact]
    public async Task Download_Refuses_WhenNoSha256IsPublished()
    {
        var downloader = new OtaPayloadDownloader(
            StubHttp(req => req.RequestUri!.AbsoluteUri.EndsWith(".sha256")
                ? new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                : Ok(MakePeBytes())),
            new StubAuthenticode(valid: true));

        var target = TempTarget();
        var result = await downloader.DownloadAsync("dlss", "310.7.128", target, OtaChannel.Production, Permitting());

        Assert.False(result.Success);
        Assert.Contains("No published .sha256", result.Error);
        Assert.False(File.Exists(target));
    }

    /// <summary>RED arm: a correctly-hashed file that is not signed by NVIDIA.</summary>
    [Fact]
    public async Task Download_Refuses_WhenSignerIsNotNvidia()
    {
        var payload = MakePeBytes();
        var digest = Sha256Hex(payload);

        var downloader = new OtaPayloadDownloader(
            StubHttp(req => req.RequestUri!.AbsoluteUri.EndsWith(".sha256") ? Ok(digest) : Ok(payload)),
            new StubAuthenticode(valid: false));

        var target = TempTarget();
        var result = await downloader.DownloadAsync("dlss", "310.7.128", target, OtaChannel.Production, Permitting());

        Assert.False(result.Success);
        Assert.Contains("Authenticode", result.Error);
        Assert.False(File.Exists(target));
    }

    /// <summary>
    /// RED arm: an HTTP 200 that is really an error page. Its digest can be made to match, so
    /// only the PE check catches it — this is why that check exists separately.
    /// </summary>
    [Fact]
    public async Task Download_Refuses_WhenPayloadIsNotAPeImage()
    {
        var notPe = System.Text.Encoding.UTF8.GetBytes("<html>404 Not Found</html>");
        var digest = Sha256Hex(notPe);

        var downloader = new OtaPayloadDownloader(
            StubHttp(req => req.RequestUri!.AbsoluteUri.EndsWith(".sha256") ? Ok(digest) : Ok(notPe)),
            new StubAuthenticode(valid: true));

        var target = TempTarget();
        var result = await downloader.DownloadAsync("dlss", "310.7.128", target, OtaChannel.Production, Permitting());

        Assert.False(result.Success);
        Assert.Contains("not a PE image", result.Error);
        Assert.False(File.Exists(target));
    }

    /// <summary>A rejected download must not leave the temp file behind either.</summary>
    [Fact]
    public async Task RejectedDownload_LeavesNoPartialFile()
    {
        var payload = MakePeBytes();

        var downloader = new OtaPayloadDownloader(
            StubHttp(req => req.RequestUri!.AbsoluteUri.EndsWith(".sha256")
                ? Ok(Sha256Hex(new byte[] { 9 }))
                : Ok(payload)),
            new StubAuthenticode(valid: true));

        var target = TempTarget();
        await downloader.DownloadAsync("dlss", "310.7.128", target, OtaChannel.Production, Permitting());

        Assert.False(File.Exists(target));
        Assert.False(File.Exists(target + ".otadownload"));
    }

    /// <summary>The real verifier must fail closed off Windows rather than waving bytes through.</summary>
    [Fact]
    public void RealVerifier_FailsClosed_OffWindows()
    {
        if (OperatingSystem.IsWindows())
            return;

        var result = new WindowsAuthenticodeVerifier().Verify("/nonexistent");
        Assert.False(result.IsValid);
    }

    private static byte[] MakePeBytes()
    {
        var bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    private static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)).ToLowerInvariant();

    // ---- consent gate ------------------------------------------------------

    /// <summary>
    /// RED arm for the flag flip. With downloads preferred but redistribution NOT accepted —
    /// exactly the state a fresh v0.73 install is in — nothing may be fetched. This is the test
    /// that makes turning the checkbox on by default safe.
    /// </summary>
    [Fact]
    public async Task Download_Refuses_WithoutRedistributionAcceptance()
    {
        var payload = MakePeBytes();
        var digest = Sha256Hex(payload);
        var reachedNetwork = false;

        var downloader = new OtaPayloadDownloader(
            StubHttp(req =>
            {
                reachedNetwork = true;
                return req.RequestUri!.AbsoluteUri.EndsWith(".sha256") ? Ok(digest) : Ok(payload);
            }),
            new StubAuthenticode(valid: true));

        var target = TempTarget();
        var settings = new AppSettings
        {
            AllowOtaPayloadDownloads = true,   // shipped default
            OtaRedistributionAccepted = false, // never defaulted
        };

        var result = await downloader.DownloadAsync(
            "dlss", "310.7.128", target, OtaChannel.Production, settings);

        Assert.False(result.Success);
        Assert.Contains("not permitted", result.Error);
        Assert.False(File.Exists(target));
        Assert.False(reachedNetwork); // refused before any request went out
    }

    /// <summary>
    /// A caller that forgets to pass settings must fail closed. The gate lives on the download
    /// path, so omitting it cannot be a way around it.
    /// </summary>
    [Fact]
    public async Task Download_Refuses_WhenSettingsAreNotSupplied()
    {
        var downloader = new OtaPayloadDownloader(
            StubHttp(_ => Ok(MakePeBytes())),
            new StubAuthenticode(valid: true));

        var result = await downloader.DownloadAsync("dlss", "310.7.128", TempTarget());

        Assert.False(result.Success);
        Assert.Contains("not permitted", result.Error);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true,  false, false)]
    [InlineData(false, true,  false)]
    [InlineData(true,  true,  true)]
    public void IsDownloadPermitted_RequiresBothFlags(bool allow, bool accepted, bool expected)
    {
        var settings = new AppSettings
        {
            AllowOtaPayloadDownloads = allow,
            OtaRedistributionAccepted = accepted,
        };

        Assert.Equal(expected, OtaPayloadDownloader.IsDownloadPermitted(settings));
    }
}
