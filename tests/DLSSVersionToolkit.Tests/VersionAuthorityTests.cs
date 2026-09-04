using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.72 — the recurrence gate.
///
/// AGENTS.md has stated since v0.0.53 that "DLL bytes are the only version authority" and listed
/// six bugs caused by breaking it. The reported grid mismatch, the contradictory completion
/// dialog, and the missing DLSSNR read were instances SEVEN, EIGHT and NINE. The rule was written
/// down but nothing enforced it, so each new surface re-derived a version from whatever was handy
/// — a sidecar config, a folder name, a channel string, the value a run intended to write.
///
/// These gates make instance ten fail in CI rather than in a screenshot. They are deliberately
/// structural (what may FEED a version) rather than behavioral (what a version happens to be),
/// because the behavior gates already exist per-surface and did not prevent recurrence.
/// </summary>
public class VersionAuthorityTests
{
    private static string Services => Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
        "DLSSVersionToolkit.Core", "Services");

    /// <summary>
    /// Version-bearing fields may only be assigned from a DLL read, a status code, or another
    /// already-validated version — never from parsed file text.
    ///
    /// The v0.68 defect in one line: `result.DLSS = ParseComponent(content, "dlss")`.
    /// </summary>
    [Fact]
    public void NoVersionField_IsAssignedFromParsedText()
    {
        var versionFields = new[] { "DLSS", "FrameGen", "DLSSD", "DeepDVC", "DLSSNR", "Streamline" };
        var textSources = new[] { "ParseComponent(", "ReadAllText(", "ReadAllLines(", "Split('\\n')" };

        var offenders = new List<string>();
        foreach (var file in Directory.GetFiles(Services, "*.cs"))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.TrimStart().StartsWith("//")) continue;

                foreach (var field in versionFields)
                {
                    // "<something>.Field = " or "Field = " on the left of an assignment.
                    if (!line.Contains($".{field} =") && !line.Contains($"{field} = ")) continue;
                    if (textSources.Any(src => line.Contains(src)))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} version field(s) assigned from parsed text rather than DLL bytes:\n" +
            string.Join("\n", offenders));
    }

    /// <summary>
    /// Every version a scanner reports must come through DllVersionReader. A local
    /// FileVersionInfo.GetVersionInfo call is a second implementation of "what version is this
    /// DLL", and two implementations drift — that is the v0.0.61 lesson applied to reads instead
    /// of to the validity regex.
    /// </summary>
    [Fact]
    public void VersionReads_GoThroughTheCanonicalReader()
    {
        var offenders = Directory.GetFiles(Services, "*.cs")
            .Where(f => Path.GetFileName(f) != "DllVersionReader.cs")
            .Where(f => File.ReadAllText(f).Contains("FileVersionInfo.GetVersionInfo("))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"{offenders.Count} service(s) read a version directly instead of via DllVersionReader: " +
            string.Join(", ", offenders));
    }

    /// <summary>
    /// The three status codes have one definition each. A fourth spelling ("n/a", "--", "none")
    /// would be a version string that compares as real — the exact hole IsReportedVersion closed.
    /// </summary>
    [Fact]
    public void StatusCodes_HaveOneDefinitionEach()
    {
        var reader = File.ReadAllText(Path.Combine(Services, "DllVersionReader.cs"));
        Assert.Contains("IsReportedVersion", reader);

        // The absent code is defined once, in the parser, and referenced elsewhere by name.
        var parser = File.ReadAllText(Path.Combine(Services, "NgxConfigParser.cs"));
        Assert.Contains("public const string VersionAbsent", parser);
        Assert.Contains("public const string VersionUnreadable", parser);

        var literalUsers = Directory.GetFiles(Services, "*.cs")
            .Where(f => Path.GetFileName(f) is not ("NgxConfigParser.cs" or "DllVersionReader.cs"))
            .Where(f => File.ReadAllText(f).Contains("\"—\""))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(literalUsers.Count == 0,
            $"{literalUsers.Count} service(s) hardcode the absent code instead of NgxConfigParser.VersionAbsent: " +
            string.Join(", ", literalUsers));
    }

    /// <summary>
    /// A completion report must be derived from the operation's EFFECT, not its INPUT. The v0.69
    /// defect was a dialog string assembled before the writes it described; this pins the reader
    /// that replaced it as the source for applied state.
    /// </summary>
    [Fact]
    public void AppliedState_IsReadFromDisk_NotFromIntent()
    {
        var vm = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit", "ViewModels", "MainViewModel.cs"));

        Assert.Contains("AppliedVersionVerifier.Verify(", vm);

        // The pre-write re-assertion text must not be interpolated into a dialog again.
        Assert.DoesNotContain("overrideLine +", vm);
    }

    /// <summary>
    /// Staging is opt-in and production is the default. The value of a pre-release channel is
    /// that the user chose it; a default that silently tracks staging would mark up-to-date
    /// machines as behind against a build the driver won't serve them.
    /// </summary>
    [Fact]
    public void OtaChannel_DefaultsToProduction_WithRecordedProvenance()
    {
        var ota = File.ReadAllText(Path.Combine(Services, "NvidiaOtaService.cs"));

        Assert.Contains("3e933c08-ea30-45ae-93d1-5114edf9c3b9", ota);
        Assert.Contains("CDNServerType", ota);     // NVIDIA's own documented prod/staging switch

        // Every channel-taking entry point defaults to Production.
        Assert.Contains("OtaChannel channel = OtaChannel.Production", ota);

        // The settings that widen behavior are off unless the user turns them on.
        var settings = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit.Core", "Models", "AppSettings.cs"));
        Assert.Contains("IncludePreReleaseChannel { get; set; } = false", settings);
        Assert.Contains("AllowOtaPayloadDownloads { get; set; } = false", settings);
    }

    /// <summary>
    /// Nothing downloaded from the OTA endpoint reaches disk without a digest AND signature
    /// check. This is a structural gate on the download path itself, not on one call site.
    /// </summary>
    [Fact]
    public void OtaPayloads_AreVerifiedBeforeInstall()
    {
        var downloader = File.ReadAllText(Path.Combine(Services, "OtaPayloadDownloader.cs"));

        Assert.Contains("No published .sha256", downloader);   // missing sidecar => refuse
        Assert.Contains("SHA-256 mismatch", downloader);        // wrong digest    => refuse
        Assert.Contains("not a PE image", downloader);          // HTML 200        => refuse
        Assert.Contains("_authenticode.Verify(", downloader);   // wrong signer    => refuse

        // The move into place happens once, after every check.
        Assert.Contains("File.Move(temp, destinationPath", downloader);
    }
}
