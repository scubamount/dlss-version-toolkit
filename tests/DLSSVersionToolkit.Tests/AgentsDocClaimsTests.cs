using DLSSVersionToolkit.Core.Services;
using System.Text.RegularExpressions;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Keeps AGENTS.md's factual claims tied to the tree (v0.0.55).
///
/// Why this exists: AGENTS.md carried the header "Auto-generated from all feature plans" for
/// months. No generator has ever existed in this repo — no .specify/, no update-agent-context
/// script — so the file read as machine-derived while going six releases stale, and the one
/// structural claim in it (window sizing) had already needed a correction commit (v0.0.49,
/// af02efa). Rewriting it by hand at v0.0.55, the FIRST draft asserted "27 core services" against
/// a real 26. A hand-counted number in prose drifts on the next commit that adds a file.
///
/// So the counts are gated rather than trusted. Prose cannot enforce anything; this can.
/// Deliberately narrow: it checks only claims that are mechanically checkable (counts and file
/// existence). Rationale and lessons are not testable and are not tested — the point is that a
/// reader can trust the numbers, which is what makes the rest worth reading.
/// </summary>
public class AgentsDocClaimsTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src")) &&
                Directory.Exists(Path.Combine(dir.FullName, "tests")))
                return dir.FullName;
        }
        throw new DirectoryNotFoundException($"Could not locate repo root from {AppContext.BaseDirectory}");
    }

    private static string AgentsText() => File.ReadAllText(Path.Combine(RepoRoot(), "AGENTS.md"));

    /// <summary>
    /// The service count in the structure diagram must match the directory it describes.
    /// This is the claim the v0.0.55 rewrite got wrong on the first pass.
    /// </summary>
    [Fact]
    public void ServiceCountClaim_MatchesTheTree()
    {
        var root = RepoRoot();
        var actual = Directory.GetFiles(
            Path.Combine(root, "src", "DLSSVersionToolkit.Core", "Services"), "*.cs").Length;

        var m = Regex.Match(AgentsText(), @"all (\d+) services");
        Assert.True(m.Success, "AGENTS.md no longer states a service count — update this test or restore the claim.");

        Assert.Equal(actual, int.Parse(m.Groups[1].Value));
    }

    /// <summary>The test-file count in the structure diagram must match tests/.</summary>
    [Fact]
    public void TestFileCountClaim_MatchesTheTree()
    {
        var root = RepoRoot();
        var actual = Directory.GetFiles(
            Path.Combine(root, "tests", "DLSSVersionToolkit.Tests"), "*.cs").Length;

        var m = Regex.Match(AgentsText(), @"xUnit, (\d+) files");
        Assert.True(m.Success, "AGENTS.md no longer states a test-file count — update this test or restore the claim.");

        Assert.Equal(actual, int.Parse(m.Groups[1].Value));
    }

    /// <summary>
    /// Every path AGENTS.md names in its structure diagram must exist. A doc that points the next
    /// reader at a file that moved is worse than one that says nothing: it reads as authoritative.
    /// </summary>
    [Fact]
    public void NamedSourceFiles_AllExist()
    {
        var root = RepoRoot();
        var text = AgentsText();
        var missing = new List<string>();

        // Files named in the diagram and the lessons, with the directory each is claimed to be in.
        var claims = new (string Dir, string File)[]
        {
            (@"src\DLSSVersionToolkit.Core\Services", "NgxPathResolver.cs"),
            (@"src\DLSSVersionToolkit.Core\Services", "NgxModelLayout.cs"),
            (@"src\DLSSVersionToolkit.Core\Services", "LocalDllImportService.cs"),
            (@"src\DLSSVersionToolkit.Core\Services", "OverrideManifestService.cs"),
            (@"src\DLSSVersionToolkit.Core\Services", "UpgradeService.cs"),
            (@"src\DLSSVersionToolkit.Core\Services", "WhitelistService.cs"),
            (@"src\DLSSVersionToolkit.Core\Services", "PresetOverrideService.cs"),
            (@"src\DLSSVersionToolkit.Core\Services", "OperationGuard.cs"),
            (@"src\DLSSVersionToolkit.Core\Models", "UpdateRunReport.cs"),
            (@"src\DLSSVersionToolkit\ViewModels", "MainViewModel.cs"),
            (@"src\DLSSVersionToolkit\Views", "UpdateAllPreflightDialog.xaml"),
            (@"src\DLSSVersionToolkit", "MainWindow.xaml"),
            (@"src\DLSSVersionToolkit", "App.xaml"),
        };

        foreach (var (dir, file) in claims)
        {
            // Only assert on files the doc actually mentions — the doc may legitimately drop one.
            if (!text.Contains(file, StringComparison.Ordinal))
                continue;

            var full = Path.Combine(root, Path.Combine(dir.Split('\\')), file);
            if (!File.Exists(full))
                missing.Add($"{dir}\\{file}");
        }

        Assert.True(missing.Count == 0,
            "AGENTS.md names source files that do not exist: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The reverse direction of NamedSourceFiles_AllExist: every View dialog in the tree must be
    /// named in AGENTS.md's structure diagram. v0.0.59 shipped ThemedMessageBox and the diagram
    /// never learned it existed — a new reader mapping "which dialogs exist" from the doc got a
    /// list that silently excluded the newest, most-central one. Enumerating the tree here means
    /// a new View without a doc line reddens CI at the commit that adds it.
    /// </summary>
    [Fact]
    public void EveryViewDialog_IsNamedInDiagram()
    {
        var root = RepoRoot();
        var text = AgentsText();
        var viewsDir = Path.Combine(root, "src", "DLSSVersionToolkit", "Views");

        // Scope to the structure diagram only: the changelog prose mentions dialog names, so a
        // whole-file search passes even when the diagram — the reader's map — omits them. That
        // is exactly how v0.0.59's ThemedMessageBox shipped: named in the release notes, absent
        // from the tree, and a whole-file gate would have been green both times.
        var diagram = Regex.Match(text, @"```text\r?\n([\s\S]*?)```").Groups[1].Value;
        Assert.True(diagram.Length > 0, "AGENTS.md no longer has a ```text structure diagram — update this test.");

        var unmentioned = Directory.GetFiles(viewsDir, "*.xaml")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(name => !diagram.Contains(name, StringComparison.Ordinal))
            .ToList();

        Assert.True(unmentioned.Count == 0,
            "Views exist that AGENTS.md's structure diagram does not name: " + string.Join(", ", unmentioned));
    }

    /// <summary>
    /// Symbols cited in the standing lessons must still exist. A lesson pointing at a renamed
    /// method sends the next reader looking for something that is not there and quietly erodes
    /// trust in every other lesson on the page.
    /// </summary>
    [Fact]
    public void CitedSymbols_StillExist()
    {
        var root = RepoRoot();
        var text = AgentsText();
        var missing = new List<string>();

        var claims = new (string Symbol, string RelPath)[]
        {
            ("WriteRoots", @"src\DLSSVersionToolkit.Core\Services\NgxPathResolver.cs"),
            ("GetWritableBase", @"src\DLSSVersionToolkit.Core\Services\NgxPathResolver.cs"),
            ("GetCandidatePaths", @"src\DLSSVersionToolkit.Core\Services\NgxPathResolver.cs"),
            ("IsApplyingPreset", @"src\DLSSVersionToolkit\ViewModels\MainViewModel.cs"),
            ("FitToWorkArea", @"src\DLSSVersionToolkit\MainWindow.xaml.cs"),
            ("ResolveBinPath", @"src\DLSSVersionToolkit.Core\Services\UpgradeService.cs"),
            ("RollForward", @"src\DLSSVersionToolkit\DLSSVersionToolkit.csproj"),
        };

        foreach (var (symbol, rel) in claims)
        {
            if (!text.Contains(symbol, StringComparison.Ordinal))
                continue;

            var full = Path.Combine(root, Path.Combine(rel.Split('\\')));
            if (!File.Exists(full) ||
                !File.ReadAllText(full).Contains(symbol, StringComparison.Ordinal))
                missing.Add($"{symbol} (expected in {rel})");
        }

        Assert.True(missing.Count == 0,
            "AGENTS.md cites symbols that no longer exist where claimed: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The gate tests AGENTS.md advertises must exist by name. The doc tells the next reader that
    /// duplicated-rule regressions are mechanically prevented; if the gate were renamed or deleted,
    /// that promise would be false and nothing else would notice.
    /// </summary>
    [Fact]
    public void AdvertisedGates_Exist()
    {
        var root = RepoRoot();
        var text = AgentsText();
        var testSources = Directory
            .GetFiles(Path.Combine(root, "tests"), "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .ToList();

        var missing = new List<string>();
        foreach (var gate in new[]
                 {
                     "NgxRootLiteral_IsDefinedOnlyInTheResolver",
                     "ImportLandedPredicate_IsNotRebuiltByCallers"
                 })
        {
            if (!text.Contains(gate, StringComparison.Ordinal))
                continue;
            if (!testSources.Any(s => s.Contains(gate, StringComparison.Ordinal)))
                missing.Add(gate);
        }

        Assert.True(missing.Count == 0,
            "AGENTS.md advertises gates that no longer exist: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The doc must not claim to be generated. It said "Auto-generated from all feature plans"
    /// while no generator existed, which is why nobody updated it by hand for six releases: the
    /// header told every reader that something else owned the file.
    /// </summary>
    [Fact]
    public void DoesNotClaimToBeAutoGenerated()
    {
        var root = RepoRoot();
        var text = AgentsText();

        var generatorExists =
            Directory.Exists(Path.Combine(root, ".specify")) ||
            Directory.GetFiles(root, "*update-agent-context*", SearchOption.AllDirectories).Length > 0;

        if (!generatorExists)
            Assert.DoesNotContain("Auto-generated", text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The version in the AGENTS.md header must match the shipping version in the csproj. This is
    /// the staleness signal itself: bump the csproj, and the doc has to be revisited or CI reddens.
    /// </summary>
    [Fact]
    public void HeaderVersion_MatchesCsproj()
    {
        var root = RepoRoot();
        var csproj = File.ReadAllText(
            Path.Combine(root, "src", "DLSSVersionToolkit", "DLSSVersionToolkit.csproj"));

        var shipping = Regex.Match(csproj, @"<Version>([^<]+)</Version>");
        Assert.True(shipping.Success, "csproj has no <Version> element.");

        var claimed = Regex.Match(AgentsText(), @"Last updated:[^(]*\(v([0-9.]+)\)");
        Assert.True(claimed.Success,
            "AGENTS.md header no longer states 'Last updated: <date> (vX.Y.Z)'.");

        Assert.Equal(shipping.Groups[1].Value, claimed.Groups[1].Value);
    }

    /// <summary>
    /// README's Supported-components line must name every canonical NGX DLL. The v0.63 commit
    /// grew the set to five and left the README saying four twice — the same silent-doc-rot the
    /// AGENTS gates exist for, in the one public-facing file nothing covered. DLL -> display name
    /// is defined HERE, once; a new NgxDllNames member without a mapping entry reddens too.
    /// </summary>
    [Fact]
    public void ReadmeComponentList_CoversNgxDllNames()
    {
        var readme = File.ReadAllText(Path.Combine(RepoRoot(), "README.md"));
        var displayByDll = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["nvngx_dlss.dll"]    = "DLSS,",          // comma: 'DLSS' must not match inside 'DLSSD'/'DLSSNR'
            ["nvngx_dlssg.dll"]   = "Frame Generation",
            ["nvngx_dlssd.dll"]   = "DLSSD",
            ["nvngx_deepdvc.dll"] = "DeepDVC",
            ["nvngx_dlssnr.dll"]  = "DLSSNR",
        };

        var missing = new List<string>();
        foreach (var dll in UpgradeService.NgxDllNames)
        {
            if (!displayByDll.TryGetValue(dll, out var name))
            {
                missing.Add($"{dll} (no display-name mapping — add one here AND to README)");
                continue;
            }
            if (!readme.Contains(name, StringComparison.Ordinal))
                missing.Add($"{dll} (README never mentions \"{name}\")");
        }

        Assert.True(missing.Count == 0,
            "README component list is stale vs UpgradeService.NgxDllNames: " + string.Join("; ", missing));
    }
}
