using DLSSVersionToolkit.Core.Models;
using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Gates for <see cref="OtaCacheScanner"/> (v0.75) — reading the components NVIDIA's own updater
/// already downloaded, instead of downloading anything ourselves.
///
/// These build a synthetic OTA cache on disk rather than mocking the filesystem, because the bug
/// being prevented is a PATH bug: <see cref="NgxScanner"/> looked only at
/// <c>models\dlss_override\versions</c> and was structurally blind to
/// <c>models\&lt;component&gt;\versions\&lt;packed&gt;\files\*.bin</c>, which is a sibling subtree.
/// A mock that answers whatever the scanner asks cannot fail that way.
///
/// The payload files carry no real PE version resource (CI cannot fabricate a signed NVIDIA DLL),
/// so version-reading asserts the "present but unreadable" branch — which is itself the v0.68
/// distinction worth pinning: unreadable must report "Unknown", never "—" (absent) and never a
/// silently-dropped row.
/// </summary>
public class OtaCacheScannerTests : IDisposable
{
    private readonly string _root;

    public OtaCacheScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "otacache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* temp cleanup is best-effort */ }
    }

    /// <summary>Creates models[\Staging]\component\versions\packed\files\leaf and returns its path.</summary>
    private string Plant(string component, string packed, string leaf, bool staging = false, string? content = null)
    {
        var parts = staging
            ? new[] { _root, "Staging", "models", component, "versions", packed, "files" }
            : new[] { _root, "models", component, "versions", packed, "files" };
        var dir = Path.Combine(parts);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, leaf);
        File.WriteAllText(path, content ?? "not-a-real-pe");
        return path;
    }

    // ---------------------------------------------------------------- the blindness itself

    /// <summary>
    /// THE regression. A payload planted where NVIDIA's updater puts one must be found. Before
    /// v0.75 nothing in the app read this tree, so this is the red arm made permanent.
    /// </summary>
    [Fact]
    public void Harvest_FindsPayload_InNvidiasOwnComponentTree()
    {
        Plant("dlss", "20318080", "160_E658700.bin");

        var found = new OtaCacheScanner().Harvest(_root);

        var hit = Assert.Single(found);
        Assert.Equal("dlss", hit.ComponentDir);
        Assert.Equal("nvngx_dlss.dll", hit.DllName);
        Assert.Equal("20318080", hit.PackedFolder);
        Assert.Equal(OtaCacheScanner.SourceProduction, hit.Source);
    }

    /// <summary>Production and staging are distinct roots and must not be conflated.</summary>
    [Fact]
    public void Harvest_SeparatesStagingFromProduction()
    {
        Plant("dlss", "20318080", "160_E658700.bin");
        Plant("dlss", "20318464", "160_E658700.bin", staging: true);

        var found = new OtaCacheScanner().Harvest(_root);

        Assert.Equal(2, found.Count);
        Assert.Single(found, p => p.Source == OtaCacheScanner.SourceProduction);
        Assert.Single(found, p => p.Source == OtaCacheScanner.SourceStaging);
    }

    /// <summary>All three OTA-delivered components are harvested, each mapped to its own DLL.</summary>
    [Fact]
    public void Harvest_CoversDlssDlssgAndDlssd()
    {
        Plant("dlss", "20318080", "160_E658700.bin");
        Plant("dlssg", "20318080", "160_E658700.bin");
        Plant("dlssd", "20318080", "160_E658700.bin");

        var found = new OtaCacheScanner().Harvest(_root);

        Assert.Equal(3, found.Count);
        Assert.Equal(
            new[] { "nvngx_dlss.dll", "nvngx_dlssd.dll", "nvngx_dlssg.dll" },
            found.Select(p => p.DllName).OrderBy(n => n, StringComparer.Ordinal).ToArray());
    }

    // ---------------------------------------------------------------- scope discipline

    /// <summary>
    /// The harvestable set must stay a SUBSET of the canonical DLL map, so adding a sixth
    /// component to <see cref="NgxModelLayout.ComponentDirByDll"/> cannot bypass the deliberate
    /// decision about which components NVIDIA's OTA channel actually delivers.
    /// </summary>
    [Fact]
    public void HarvestableComponents_AreSubsetOfCanonicalMap_WithMatchingDirs()
    {
        foreach (var (dll, dir) in OtaCacheScanner.HarvestableComponents)
        {
            Assert.True(NgxModelLayout.ComponentDirByDll.ContainsKey(dll),
                $"{dll} is harvested but absent from the canonical ComponentDirByDll map.");
            Assert.Equal(NgxModelLayout.ComponentDirByDll[dll], dir);
        }
    }

    /// <summary>
    /// dlssnr appears NOWHERE in NVIDIA's OTA channel — a live bucket listing returned zero keys
    /// for it, independently confirming the v0.65 decision to exclude NR from channelByDll. So a
    /// planted NR payload must be ignored, and its absence is normal rather than an error.
    /// </summary>
    [Fact]
    public void Harvest_IgnoresDlssnr_WhichTheOtaChannelDoesNotCarry()
    {
        Plant("dlssnr", "20318080", "160_E658700.bin");
        Plant("deepdvc", "20318080", "160_E658700.bin");

        var errors = new List<string>();
        var found = new OtaCacheScanner().Harvest(_root, errors);

        Assert.Empty(found);
        Assert.Empty(errors);   // Absence is not a failure.
    }

    /// <summary>
    /// The .zip components (dlss_override, sl_sdk_0) are a different payload shape and are out of
    /// scope for this scanner; a .zip leaf must not be mistaken for a renamed DLL.
    /// </summary>
    [Fact]
    public void Harvest_IgnoresZipBundles_AndSha256Sidecars()
    {
        Plant("dlss", "20318080", "160_E658700.zip");
        Plant("dlss", "20318080", "160_E658700.bin.sha256");

        Assert.Empty(new OtaCacheScanner().Harvest(_root));
    }

    // ---------------------------------------------------------------- filename shapes

    /// <summary>
    /// Arch prefix and app id are matched as hex, never pinned to the two generic ids. The live
    /// channel carries prefixes 160/170/180/190/1B0 plus a long tail of per-title CMS ids;
    /// hardcoding E658700/E658703 would silently skip every per-title payload on the machine.
    /// </summary>
    [Theory]
    [InlineData("160_E658700.bin", true)]   // NGX generic
    [InlineData("160_E658703.bin", true)]   // Streamline generic
    [InlineData("170_E658700.bin", true)]   // Ampere
    [InlineData("1B0_E658700.bin", true)]   // Blackwell2
    [InlineData("160_B9D48D0.bin", true)]   // per-title CMS id, real example
    [InlineData("160_E99B5EC.bin", true)]   // per-title CMS id, real example
    [InlineData("nvngx_dlss.dll", false)]   // real-named DLL is not an OTA leaf
    [InlineData("160_E658700.zip", false)]  // bundle, not a renamed DLL
    [InlineData("readme.txt", false)]
    [InlineData("160E658700.bin", false)]   // missing separator
    public void Harvest_AcceptsAnyHexArchAndAppId_RejectsEverythingElse(string leaf, bool expected)
    {
        Plant("dlss", "20318080", leaf);

        var found = new OtaCacheScanner().Harvest(_root);

        Assert.Equal(expected, found.Count == 1);
    }

    /// <summary>A non-packed version folder name is not ours to interpret and must be skipped.</summary>
    [Fact]
    public void Harvest_SkipsNonPackedVersionFolders()
    {
        Plant("dlss", "310.7.128.0", "160_E658700.bin");
        Plant("dlss", ".dlss-backup-20260904", "160_E658700.bin");

        Assert.Empty(new OtaCacheScanner().Harvest(_root));
    }

    // ---------------------------------------------------------------- version authority

    /// <summary>
    /// A payload whose bytes carry no readable version resource reports "Unknown" — present but
    /// unreadable — and is still returned. It must never report "—" (absent) and must never be
    /// dropped: conflating those two was the v0.68/v0.69 defect.
    /// </summary>
    [Fact]
    public void Harvest_UnreadablePayload_ReportsUnknown_NotAbsent_AndIsKept()
    {
        Plant("dlss", "20318080", "160_E658700.bin");

        var hit = Assert.Single(new OtaCacheScanner().Harvest(_root));

        Assert.Equal(NgxConfigParser.VersionUnreadable, hit.Version);
        Assert.False(hit.VersionFromBytes);
        Assert.False(DllVersionReader.IsReportedVersion(hit.Version));
    }

    /// <summary>
    /// The folder name is NOT the version. This is the legacy-generation trap: a real channel
    /// listing carries 131356 (decodes to 2.1.28, a legacy DLSS 2.x build) as a sibling of
    /// 20318080 (310.7.128). Both must be harvested, and neither may have its packed folder name
    /// promoted into the version field — otherwise a 2.x tree outranks a 310.x one.
    /// </summary>
    [Fact]
    public void Harvest_DoesNotTreatPackedFolderNameAsTheVersion()
    {
        Plant("dlss", "20318080", "160_E658700.bin");
        Plant("dlss", "131356", "160_E658700.bin");

        var found = new OtaCacheScanner().Harvest(_root);

        Assert.Equal(2, found.Count);
        Assert.All(found, p => Assert.Equal(NgxConfigParser.VersionUnreadable, p.Version));
        Assert.All(found, p => Assert.DoesNotContain(p.Version, new[] { "20318080", "131356" }));
        // The packed name survives as a diagnostic, which is its only legitimate use here.
        Assert.Contains(found, p => p.PackedFolder == "131356");
        Assert.Contains(found, p => p.PackedFolder == "20318080");
    }

    /// <summary>
    /// Sanity on the packed decoder against values taken from a real bucket listing, including the
    /// legacy sibling that makes folder-name sorting unsafe.
    /// </summary>
    [Theory]
    [InlineData("20318080", 310, 7, 128)]
    [InlineData("20316673", 310, 2, 1)]
    [InlineData("20317696", 310, 6, 0)]
    [InlineData("131356", 2, 1, 28)]        // legacy generation, same directory
    [InlineData("132874", 2, 7, 10)]
    public void PackedFolderNames_FromLiveListing_DecodeAsDocumented(
        string packed, int major, int minor, int patch)
    {
        var decoded = NgxModelLayout.DecodePackedVersion(packed);

        Assert.NotNull(decoded);
        Assert.Equal(new Version(major, minor, patch), decoded);
    }

    // ---------------------------------------------------------------- grid projection

    /// <summary>
    /// Components version independently in the OTA cache, so a row reports "—" for a component
    /// that genuinely is not cached at that version, and "N/A" for the two the channel never
    /// carries. Three distinct meanings, per v0.68.
    /// </summary>
    [Fact]
    public void ToEntries_MarksMissingComponentsAbsent_AndUncarriedOnesNotApplicable()
    {
        Plant("dlss", "20318080", "160_E658700.bin");   // dlss only; no dlssg/dlssd

        var scanner = new OtaCacheScanner();
        var entry = Assert.Single(scanner.ToEntries(scanner.Harvest(_root)));

        Assert.Equal(OtaCacheScanner.SourceProduction, entry.Source);
        Assert.Equal(NgxConfigParser.VersionUnreadable, entry.DLSS);   // present, unreadable
        Assert.Equal(NgxConfigParser.VersionAbsent, entry.FrameGen);   // absent
        Assert.Equal(NgxConfigParser.VersionAbsent, entry.DLSSD);      // absent
        Assert.Equal("N/A", entry.DLSSNR);       // channel does not carry it
        Assert.Equal("N/A", entry.DeepDVC);      // channel does not carry it
        Assert.Equal("N/A", entry.Streamline);   // not in the nvngx component tree
    }

    /// <summary>Harvesting must never be able to write. The cache is a read source, full stop.</summary>
    [Fact]
    public void OtaCacheRoots_AreNeverWritableTargets()
    {
        var roots = new[] { @"C:\ProgramData\NVIDIA\NGX" };

        Assert.False(NgxPathResolver.IsWritableRoot(
            @"C:\ProgramData\NVIDIA\NGX-ota-elsewhere\models\dlss", roots));

        var scannerSource = File.ReadAllText(FindRepoFile("OtaCacheScanner.cs"));
        foreach (var forbidden in new[]
                 {
                     "File.Copy", "File.WriteAllBytes", "File.WriteAllText", "File.Delete",
                     "Directory.CreateDirectory", "File.Move", "File.Create("
                 })
        {
            Assert.False(scannerSource.Contains(forbidden, StringComparison.Ordinal),
                $"OtaCacheScanner must be read-only but contains {forbidden}. " +
                "Installing a harvested payload belongs in LocalDllImportService, the one write funnel.");
        }
    }

    private static string FindRepoFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(
                dir.FullName, "src", "DLSSVersionToolkit.Core", "Services", fileName);
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException($"Could not locate {fileName} from {AppContext.BaseDirectory}");
    }

    // ---------------------------------------------------------------- wiring

    /// <summary>
    /// The scanner must have a PRODUCTION consumer, not just tests.
    ///
    /// This gate exists because of a defect shipped in v0.73: <c>AllowOtaPayloadDownloads</c> was
    /// a settings flag whose only readers were the settings dialog and tests —
    /// <c>OtaPayloadDownloader</c> was never constructed by production code, so the checkbox
    /// gated nothing and flipping its default was inert. A scanner nothing calls is the same
    /// defect wearing different clothes: every test here would stay green while the feature did
    /// nothing on a user's machine.
    /// </summary>
    [Fact]
    public void OtaCacheScanner_IsConsumedByScanService_NotOnlyByTests()
    {
        var scanService = File.ReadAllText(FindRepoFile("ScanService.cs"));

        Assert.Contains("OtaCacheScanner", scanService, StringComparison.Ordinal);
        Assert.Contains(".Harvest(", scanService, StringComparison.Ordinal);
        Assert.Contains(".ToEntries(", scanService, StringComparison.Ordinal);
    }

    /// <summary>
    /// Harvested rows must dedupe by (Source, BuildID), never by Source alone.
    ///
    /// ScanService's pre-existing NGX loop dedupes by source name, which is correct when a source
    /// yields one row. The OTA cache yields MANY versions under a single source tag, so reusing
    /// that rule would keep the first version found and silently discard every other one — the
    /// user would see exactly one OTA row no matter how many the driver had cached.
    /// </summary>
    [Fact]
    public void ScanService_DedupesHarvestedRowsByVersion_NotBySourceAlone()
    {
        var scanService = File.ReadAllText(FindRepoFile("ScanService.cs"));
        var normalized = string.Join(" ", scanService.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains("s.Source == entry.Source && s.BuildID == entry.BuildID", normalized,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Two OTA versions of the same component produce two rows, ordered newest first. Direct
    /// proof of the behavior the wiring gate above protects.
    /// </summary>
    [Fact]
    public void ToEntries_KeepsEveryCachedVersion_AsItsOwnRow()
    {
        var scanner = new OtaCacheScanner();
        var payloads = new[]
        {
            new OtaCacheScanner.HarvestedPayload("dlss", "nvngx_dlss.dll", "310.6.0.0",
                "20317696", Path.Combine(_root, "a", "160_E658700.bin"), OtaCacheScanner.SourceProduction),
            new OtaCacheScanner.HarvestedPayload("dlss", "nvngx_dlss.dll", "310.7.128.0",
                "20318080", Path.Combine(_root, "b", "160_E658700.bin"), OtaCacheScanner.SourceProduction),
        };

        var entries = scanner.ToEntries(payloads);

        Assert.Equal(2, entries.Count);
        Assert.Equal("310.7.128.0", entries[0].BuildID);   // newest first
        Assert.Equal("310.6.0.0", entries[1].BuildID);
    }

    /// <summary>
    /// Both OTA sources need a human-readable grid label, and the pre-release one must say so —
    /// a staging build shown with a production label would misrepresent what the user is looking at.
    /// </summary>
    [Fact]
    public void OtaSources_HaveDistinctDisplayNames_AndStagingIsLabelledPreRelease()
    {
        var production = new DLSSVersionEntry { Source = OtaCacheScanner.SourceProduction };
        var staging = new DLSSVersionEntry { Source = OtaCacheScanner.SourceStaging };

        Assert.Equal("NVIDIA OTA cache", production.DisplaySource);
        Assert.Equal("NVIDIA OTA cache (pre-release)", staging.DisplaySource);
        Assert.NotEqual(production.DisplaySource, staging.DisplaySource);
        // Never show the raw tag to a user.
        Assert.DoesNotContain("_", production.DisplaySource);
    }
}
