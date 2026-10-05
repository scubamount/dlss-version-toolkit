using System.Net;
using DLSSVersionToolkit.Core.Models;
using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.78 regression gates. The runtime check exists so a future release on a newer .NET can
/// never be swapped onto a machine that cannot start it: the updater replaces the running exe,
/// so a new exe that fails to launch leaves the user with no working app.
/// </summary>
public class V078RegressionTests
{
    private static readonly Version V9 = new(9, 0, 0);

    private static Func<string, IEnumerable<Version>> Installed(params string[] versions) =>
        _ => versions.Select(Version.Parse);

    private static IReadOnlyList<AppUpdateService.RuntimeRequirement> Req(string version, string rollForward = "Minor") =>
        new[] { new AppUpdateService.RuntimeRequirement("Microsoft.WindowsDesktop.App", Version.Parse(version), rollForward) };

    // ---- FindMissingRuntime: the host's roll-forward rules, boundary-first ----------------

    [Theory]
    [InlineData("10.0.0", "Minor", new[] { "9.0.9" }, false)]          // the case this exists for
    [InlineData("10.0.0", "Minor", new[] { "10.0.0" }, true)]          // exact boundary
    [InlineData("10.0.0", "Minor", new[] { "10.0.3" }, true)]          // newer patch
    [InlineData("10.0.0", "Minor", new[] { "11.0.0" }, false)]         // Minor never crosses a major
    [InlineData("10.0.0", "LatestMajor", new[] { "11.0.0" }, true)]    // LatestMajor does
    [InlineData("9.0.0", "LatestMajor", new[] { "10.0.0" }, true)]     // today's net9 build on a net10-only box
    [InlineData("9.0.0", "LatestMajor", new[] { "8.0.11" }, false)]    // never rolls backward
    [InlineData("10.0.0", "Minor", new string[0], false)]              // nothing installed
    [InlineData("10.1.0", "LatestPatch", new[] { "10.2.0" }, false)]   // LatestPatch pins major.minor
    public void FindMissingRuntime_FollowsRollForward(string min, string roll, string[] installed, bool satisfied)
    {
        var missing = AppUpdateService.FindMissingRuntime(Req(min, roll), Installed(installed));
        Assert.Equal(satisfied, missing == null);
        if (!satisfied)
            Assert.Equal($"Microsoft.WindowsDesktop.App {Version.Parse(min).Major}.{Version.Parse(min).Minor}", missing);
    }

    // ---- ParseRuntimeConfig: both shapes the SDK writes, and fail-closed -----------------

    [Fact]
    public void ParseRuntimeConfig_ReadsTheFrameworksArray()
    {
        const string json = """
        {"runtimeOptions":{"tfm":"net10.0","rollForward":"LatestMajor","frameworks":[
          {"name":"Microsoft.NETCore.App","version":"10.0.0"},
          {"name":"Microsoft.WindowsDesktop.App","version":"10.0.0"}]}}
        """;
        var r = AppUpdateService.ParseRuntimeConfig(json)!;
        Assert.Equal(2, r.Count);
        Assert.All(r, x => Assert.Equal("LatestMajor", x.RollForward));
        Assert.Equal(new Version(10, 0, 0), r[1].MinVersion);
    }

    [Fact]
    public void ParseRuntimeConfig_ReadsTheSingleFrameworkForm_DefaultingRollForwardToMinor()
    {
        var r = AppUpdateService.ParseRuntimeConfig(
            """{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"9.0.0"}}}""")!;
        Assert.Single(r);
        Assert.Equal("Minor", r[0].RollForward);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{"runtimeOptions":{}}""")]
    [InlineData("""{"runtimeOptions":{"framework":{"name":"Microsoft.NETCore.App","version":"ten"}}}""")]
    public void ParseRuntimeConfig_Unreadable_ReturnsNull(string json) =>
        Assert.Null(AppUpdateService.ParseRuntimeConfig(json));

    // ---- The real probe on the CI runner -------------------------------------------------

    [Fact]
    public void GetInstalledFrameworks_FindsTheRuntimeRunningThisTest()
    {
        // The test host runs on Microsoft.NETCore.App; the probe must see that exact version.
        var running = Environment.Version;
        var found = AppUpdateService.GetInstalledFrameworks("Microsoft.NETCore.App").ToList();
        Assert.Contains(found, v => v.Major == running.Major && v.Minor == running.Minor && v.Build == running.Build);
        Assert.Empty(AppUpdateService.GetInstalledFrameworks("No.Such.Framework"));
    }

    // ---- End to end through CheckForUpdateAsync -----------------------------------------

    private const string Api = "https://api.github.com/repos/scubamount/dlss-version-toolkit/releases/latest";
    private const string Exe = "https://updates.example.com/DLSSVersionToolkit.exe";
    private const string Sha = "https://updates.example.com/DLSSVersionToolkit.exe.sha256";
    private const string Cfg = "https://updates.example.com/DLSSVersionToolkit.runtimeconfig.json";

    private sealed class Handler : HttpMessageHandler
    {
        public readonly Dictionary<string, (HttpStatusCode, string)> Map = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(Map.TryGetValue(r.RequestUri!.ToString(), out var v)
                ? new HttpResponseMessage(v.Item1) { Content = new StringContent(v.Item2) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static Handler Release(bool withConfig)
    {
        var h = new Handler();
        var cfgAsset = withConfig ? $$""",{"name":"DLSSVersionToolkit.runtimeconfig.json","browser_download_url":"{{Cfg}}","size":300}""" : "";
        h.Map[Api] = (HttpStatusCode.OK, $$"""
            {"tag_name":"v99.0","body":"","assets":[
              {"name":"DLSSVersionToolkit.exe","browser_download_url":"{{Exe}}","size":100},
              {"name":"DLSSVersionToolkit.exe.sha256","browser_download_url":"{{Sha}}","size":89}{{cfgAsset}}]}
            """);
        return h;
    }

    [Fact]
    public async Task CheckForUpdate_ReleaseNeedsAnAbsentRuntime_ReportsIt_AndApplyRefusesBeforeDownload()
    {
        var h = Release(withConfig: true);
        h.Map[Cfg] = (HttpStatusCode.OK,
            """{"runtimeOptions":{"rollForward":"Minor","framework":{"name":"Microsoft.NETCore.App","version":"999.0.0"}}}""");
        var svc = new AppUpdateService(new HttpClient(h));

        var info = await svc.CheckForUpdateAsync();
        Assert.True(info.IsUpdateAvailable);
        Assert.Equal("Microsoft.NETCore.App 999.0", info.MissingRuntime);

        var result = await svc.DownloadAndApplyAsync(info);
        Assert.False(result.Success);
        Assert.Contains("Nothing was downloaded or changed", result.ErrorMessage);
    }

    [Fact]
    public async Task CheckForUpdate_ReleaseNeedsTheRunningRuntime_IsAllowed()
    {
        var h = Release(withConfig: true);
        var v = Environment.Version;
        h.Map[Cfg] = (HttpStatusCode.OK,
            "{\"runtimeOptions\":{\"framework\":{\"name\":\"Microsoft.NETCore.App\",\"version\":\"" +
            $"{v.Major}.{v.Minor}.0" + "\"}}}");
        var info = await new AppUpdateService(new HttpClient(h)).CheckForUpdateAsync();
        Assert.True(info.IsUpdateAvailable);
        Assert.Equal("", info.MissingRuntime);
    }

    [Fact]
    public async Task CheckForUpdate_RuntimeConfigUnreadable_OffersNoUpdate()
    {
        var h = Release(withConfig: true);
        h.Map[Cfg] = (HttpStatusCode.OK, "<html>rate limited</html>");
        var info = await new AppUpdateService(new HttpClient(h)).CheckForUpdateAsync();
        Assert.False(info.IsUpdateAvailable);
    }

    [Fact]
    public async Task CheckForUpdate_PreV078ReleaseWithoutConfig_StillOffered()
    {
        var info = await new AppUpdateService(new HttpClient(Release(withConfig: false))).CheckForUpdateAsync();
        Assert.True(info.IsUpdateAvailable);
        Assert.Equal("", info.MissingRuntime);
    }

    [Fact]
    public void MissingRuntimeMessage_NamesTheWingetPackage()
    {
        var msg = AppUpdateService.MissingRuntimeMessage(new AppUpdateInfo
        { LatestVersion = "0.80", MissingRuntime = "Microsoft.WindowsDesktop.App 10.0" });
        Assert.Contains("winget install Microsoft.DotNet.DesktopRuntime.10", msg);
    }

    // ---- release.yml: least privilege and the asset the updater reads --------------------

    private static string Workflow(string name) => File.ReadAllText(Path.Combine(
        Directory.GetParent(SiblingSweepTests.FindRepoSubdir("src"))!.FullName, ".github", "workflows", name));

    [Fact]
    public void ReleaseWorkflow_OnlyThePublishJobCanWrite_AndActionsArePinnedToShas()
    {
        var yml = Workflow("release.yml").Replace("\r\n", "\n");
        var build = yml[yml.IndexOf("\n  build:", StringComparison.Ordinal)..yml.IndexOf("\n  publish:", StringComparison.Ordinal)];
        Assert.DoesNotContain("contents: write", build);
        Assert.Contains("contents: read", yml[..yml.IndexOf("\njobs:", StringComparison.Ordinal)]);
        Assert.Contains("DLSSVersionToolkit.runtimeconfig.json", yml[yml.IndexOf("\n  publish:", StringComparison.Ordinal)..]);

        foreach (var file in new[] { "release.yml", "ci.yml" })
        {
            var unpinned = Workflow(file).Split('\n')
                .Where(l => l.TrimStart().StartsWith("uses:", StringComparison.Ordinal))
                .Where(l => !System.Text.RegularExpressions.Regex.IsMatch(l, @"@[0-9a-f]{40}\b"))
                .ToList();
            Assert.True(unpinned.Count == 0, $"{file}: unpinned actions: {string.Join(" | ", unpinned)}");
        }
    }
}
