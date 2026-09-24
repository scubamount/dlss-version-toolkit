using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.71 — "LATEST AVAILABLE" consults NVIDIA's OTA channel, not just GitHub.
///
/// GitHub's release feeds publish the SDK (what a developer builds against). NVIDIA's NGX OTA
/// channel publishes what the driver actually loads. They disagree by design: when this landed,
/// GitHub's newest DLSS was 310.7.0 while OTA served 310.7.128, and Streamline was 2.12.0 vs
/// 2.12.128. Comparing only against GitHub meant the app could say "UP TO DATE" against a number
/// that was not the newest NVIDIA ships.
///
/// The manifest format below is not invented — it was fetched from
/// https://ngx.download.nvidia.com/&lt;channel&gt;/org/nvidia/team/ngx/models/config/versions/2/files/nvngx_server_config.txt
/// and verified end-to-end (packed folder integers resolved to real payloads, the published
/// .sha256 matched an 89 MB download, and the DLL inside carried a matching PE FileVersion).
/// </summary>
public class OtaManifestTests
{
    /// <summary>Verbatim shape of the production manifest, trimmed to the relevant sections.</summary>
    private const string SampleManifest = """
        [dlss]
        app_865EFBC = 2.1.201
        app_B9DB490 = 2.3.4
        app_E658700 = 310.7.128

        [dlssg]
        app_E658700 = 310.7.128

        [dlssd]
        app_E658700 = 310.7.128

        [dlss_override]
        app_E658700 = 310.7.128

        [sl_sdk_0]
        app_E658703 = 2.12.128

        [force_add_update]
        app_B9D48D0 = dlss
        """;

    [Fact]
    public void Parse_ReadsGenericAppIds_PerComponent()
    {
        var versions = NvidiaOtaService.Parse(SampleManifest);

        Assert.Equal("310.7.128", versions.Single(v => v.Component == "dlss").Version);
        Assert.Equal("310.7.128", versions.Single(v => v.Component == "dlssg").Version);
        Assert.Equal("310.7.128", versions.Single(v => v.Component == "dlssd").Version);
        Assert.Equal("2.12.128", versions.Single(v => v.Component == "sl_sdk_0").Version);
    }

    [Fact]
    public void Parse_IgnoresPerTitlePins()
    {
        // Most of the manifest is per-title version pinning (app_<CMSID>). Those describe what
        // one specific game gets and are NOT "the latest available version" — reporting one
        // would show a random old build as the newest.
        var versions = NvidiaOtaService.Parse(SampleManifest);
        var dlss = versions.Single(v => v.Component == "dlss");

        Assert.Equal("310.7.128", dlss.Version);
        Assert.NotEqual("2.1.201", dlss.Version);
        Assert.NotEqual("2.3.4", dlss.Version);
    }

    [Fact]
    public void Parse_SkipsNonVersionValues()
    {
        // [force_add_update] maps app ids to component NAMES ("dlss"), not versions.
        var versions = NvidiaOtaService.Parse(SampleManifest);
        Assert.DoesNotContain(versions, v => v.Component == "force_add_update");
    }

    [Fact]
    public void Parse_HandlesInlineCommentsAndBlankInput()
    {
        var withComment = "[dlss]\napp_E658700 = 310.7.128      ; generic/default app id\n";
        Assert.Equal("310.7.128", NvidiaOtaService.Parse(withComment).Single().Version);

        Assert.Empty(NvidiaOtaService.Parse(""));
        Assert.Empty(NvidiaOtaService.Parse("   "));
        Assert.Empty(NvidiaOtaService.Parse("no sections here\njust = noise\n"));
    }

    /// <summary>
    /// The trap that makes this whole feature necessary to get right. OTA build numbers reach
    /// three digits (310.7.128), and 310.129.0 is NEWER than 310.9.0 while sorting BEFORE it as
    /// a string. A lexical comparison would report an upgrade as a downgrade and pin the header
    /// to a stale version.
    /// </summary>
    [Theory]
    [InlineData("310.9.0", "310.7.128", 1)]      // 9 > 7
    [InlineData("310.129.0", "310.9.0", 1)]      // 129 > 9 numerically; "129" < "9" lexically
    [InlineData("310.7.128", "310.7.0", 1)]      // build number counts
    [InlineData("310.7.0", "310.7.128", -1)]
    [InlineData("2.14.0", "2.12.128", 1)]
    [InlineData("2.12.128", "2.12.0", 1)]
    [InlineData("310.7.128", "310.7.128", 0)]
    [InlineData("310.7", "310.7.0.0", 0)]        // short forms pad, they don't lose
    public void CompareVersions_IsNumeric_NotLexical(string a, string b, int expected)
        => Assert.Equal(expected, Math.Sign(NvidiaOtaService.CompareVersions(a, b)));

    [Fact]
    public void CompareVersions_HandlesNullAndGarbage_WithoutThrowing()
    {
        Assert.Equal(0, NvidiaOtaService.CompareVersions(null, null));
        Assert.Equal(0, NvidiaOtaService.CompareVersions("", ""));
        Assert.True(NvidiaOtaService.CompareVersions("310.7.0", null) > 0);
        Assert.True(NvidiaOtaService.CompareVersions("310.7.0", "garbage") > 0);
    }

    /// <summary>
    /// Production channel, not staging. The dev-models root runs ahead of production — but a
    /// staging build is not an update available to the user, and prompting toward one produces an
    /// update that can never be satisfied by the driver.
    ///
    /// v0.76: production is a candidate LIST, resolved at fetch time. The single pinned root
    /// `3e933c08…` began returning 404 NoSuchKey for the manifest and every payload, while
    /// `d6e9b45e…` served a live manifest — and the app, querying only the dead one, silently fell
    /// back to GitHub-only. Both roots must stay candidates (NVIDIA has moved between them in both
    /// directions), the live one first, and staging must never be among them.
    /// </summary>
    [Fact]
    public void UsesProductionChannel_NotStaging()
    {
        var candidates = NvidiaOtaService.ProductionChannelCandidates;

        Assert.Equal("d6e9b45e-d4f6-4a84-a460-bf61decae3e8", candidates[0]);
        Assert.Contains("3e933c08-ea30-45ae-93d1-5114edf9c3b9", candidates);
        Assert.DoesNotContain(NvidiaOtaService.StagingChannel, candidates);
        Assert.Contains(NvidiaOtaService.ProductionChannel, candidates);
        Assert.Equal(new[] { NvidiaOtaService.StagingChannel },
            NvidiaOtaService.CandidateRootsFor(OtaChannel.Staging));
    }

    // ---- root resolution (v0.76) -------------------------------------------

    /// <summary>Serves a canned response per URL substring; everything else is 404.</summary>
    private sealed class RoutedHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _routes;
        public readonly List<string> Requested = new();
        public RoutedHandler(Dictionary<string, string> routes) => _routes = routes;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requested.Add(url);
            foreach (var (key, body) in _routes)
                if (url.Contains(key))
                    return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent(body),
                    });
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        }
    }

    private static string Manifest(string dlss) => $"[dlss]\napp_E658700 = {dlss}\n";

    /// <summary>
    /// The exact failure observed live: the first-listed root 404s. The service must fall through
    /// to the next candidate instead of reporting "no OTA opinion".
    /// </summary>
    [Fact]
    public async Task DeadRoot_FallsThroughToLiveRoot()
    {
        var handler = new RoutedHandler(new()
        {
            [NvidiaOtaService.ProductionChannelCandidates[1]] = Manifest("310.9.1"),
        });
        var svc = new NvidiaOtaService(new HttpClient(handler));

        var v = await svc.GetComponentVersionAsync("dlss", OtaChannel.Production);

        Assert.Equal("310.9.1", v);
        Assert.Null(svc.GetLastError(OtaChannel.Production));
        Assert.Equal(NvidiaOtaService.ProductionChannelCandidates[1], svc.ResolvedRootFor(OtaChannel.Production));
        Assert.Contains(handler.Requested, u => u.Contains(NvidiaOtaService.ProductionChannelCandidates[0]));
    }

    /// <summary>Two live roots: the one publishing the newer DLSS wins, whatever the list order.</summary>
    [Fact]
    public async Task TwoLiveRoots_NewestManifestWins()
    {
        var handler = new RoutedHandler(new()
        {
            [NvidiaOtaService.ProductionChannelCandidates[0]] = Manifest("310.6.0"),
            [NvidiaOtaService.ProductionChannelCandidates[1]] = Manifest("310.10.0"),
        });
        var svc = new NvidiaOtaService(new HttpClient(handler));

        Assert.Equal("310.10.0", await svc.GetComponentVersionAsync("dlss", OtaChannel.Production));
    }

    /// <summary>
    /// Every root dead: the result is still null (non-fatal), but the failure is REPORTED —
    /// v0.75 and earlier wrote it to Debug only, which is how a dead endpoint went unnoticed.
    /// </summary>
    [Fact]
    public async Task AllRootsDead_ReportsWhy()
    {
        var svc = new NvidiaOtaService(new HttpClient(new RoutedHandler(new())));

        Assert.Null(await svc.GetComponentVersionAsync("dlss", OtaChannel.Production));
        var error = svc.GetLastError(OtaChannel.Production);
        Assert.NotNull(error);
        Assert.Contains("HTTP 404", error);
    }

    /// <summary>
    /// OTA is an undocumented endpoint, so every path through it must be non-fatal and must not
    /// download payloads — reading version metadata is a different risk from pulling executables
    /// into %ProgramData%.
    /// </summary>
    [Fact]
    public void OtaService_ReadsMetadataOnly_AndFailsSoft()
    {
        var src = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit.Core", "Services", "NvidiaOtaService.cs"));

        // No payload download path.
        Assert.DoesNotContain(".zip", src);
        Assert.DoesNotContain("File.WriteAllBytes", src);
        Assert.DoesNotContain("ExtractToDirectory", src);

        // Fetch failures return null/empty rather than throwing at the caller.
        Assert.Contains("catch (Exception ex)", src);
        Assert.Contains("return null;", src);
    }

    /// <summary>
    /// The header must attribute which feed produced the number. Two sources that legitimately
    /// disagree, presented as one anonymous figure, is how "310.7.128 when GitHub says 310.7.0"
    /// reads as a bug rather than as information.
    /// </summary>
    [Fact]
    public void Header_LabelsWhichSourceWon()
    {
        var vm = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit", "ViewModels", "MainViewModel.cs"));
        var xaml = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit", "MainWindow.xaml"));

        Assert.Contains("_dlssLatestSource", vm);
        // v0.72: the OTA label now distinguishes production from staging, so the assertion is on
        // the prefix rather than the exact literal — "OTA" and "OTA pre-release" both qualify.
        Assert.Contains("DlssLatestSource = otaDlssResult!.IsPreRelease ? \"OTA pre-release\" : \"OTA\"", vm);
        Assert.Contains("DlssLatestSource = \"GitHub\"", vm);
        Assert.Contains("{Binding DlssLatestSource}", xaml);
    }

    /// <summary>
    /// The OTA lookup must be consulted for both components the app tracks. Wiring gate: RED
    /// against v0.70, where neither call existed.
    ///
    /// v0.72 moved these to GetNewestAsync, which folds the staging channel in when the user has
    /// enabled it. The invariant is unchanged — both components are still consulted — so the gate
    /// tracks the new call rather than being deleted.
    /// </summary>
    [Fact]
    public void ScanAsync_ConsultsOta_ForDlssAndStreamline()
    {
        var vm = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("_otaService.GetNewestAsync(", vm);
        Assert.Contains("\"dlss\", _settingsService.GetCached().IncludePreReleaseChannel", vm);
        Assert.Contains("\"sl_sdk_0\", _settingsService.GetCached().IncludePreReleaseChannel", vm);
    }
}
