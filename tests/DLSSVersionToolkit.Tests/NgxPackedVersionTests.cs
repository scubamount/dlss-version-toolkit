using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Pins NVIDIA's packed NGX version-folder encoding and the model-tree layout (v0.0.51).
///
/// Ground truth comes from two independent sources, and every decode case below is a real observed
/// value rather than a constructed one:
///   1. A nvidiaDlssGlom ("NVIDIA DLSS Override Update Tool" v2.4.10.2) run log, cross-checked
///      against the version strings its own UI displayed for the same DLLs.
///   2. emoose/DLSSTweaks issue #137, which documents folder 198400 holding DLSS 3.7.0.
/// </summary>
public class NgxPackedVersionTests
{
    // ---- decode: the four independently-sourced data points -------------------------------

    [Theory]
    // glom log wrote dlss/dlssg/dlssd here; its UI showed nvngx_dlss.dll 310.7.128.0
    [InlineData("20318080", 310, 7, 128)]
    // same log wrote deepdvc to a DIFFERENT folder; UI showed nvngx_deepdvc.dll 310.7.0.0
    [InlineData("20317952", 310, 7, 0)]
    // same log wrote every sl_* component here; UI showed sl.common.dll 2.12.0.0
    [InlineData("134144", 2, 12, 0)]
    // emoose/DLSSTweaks#137: this folder made the driver load DLSS 3.7.0
    [InlineData("198400", 3, 7, 0)]
    public void DecodePackedVersion_MatchesObservedGroundTruth(string folder, int major, int minor, int patch)
    {
        var decoded = NgxModelLayout.DecodePackedVersion(folder);

        Assert.NotNull(decoded);
        Assert.Equal(major, decoded!.Major);
        Assert.Equal(minor, decoded.Minor);
        Assert.Equal(patch, decoded.Build);
    }

    [Theory]
    [InlineData(310, 7, 128, "20318080")]
    [InlineData(310, 7, 0, "20317952")]
    [InlineData(2, 12, 0, "134144")]
    [InlineData(3, 7, 0, "198400")]
    public void EncodePackedVersion_RoundTripsObservedGroundTruth(int major, int minor, int patch, string expected)
    {
        Assert.Equal(expected, NgxModelLayout.EncodePackedVersion(major, minor, patch).ToString());
    }

    [Theory]
    [InlineData("310.7.128", "20318080")]
    [InlineData("310.7.128.0", "20318080")]   // 4th component has no field in the encoding
    [InlineData("310.7.0.0", "20317952")]
    [InlineData("310,7,128,0", "20318080")]   // comma-separated version resources
    public void EncodePackedFolderName_ParsesDottedForms(string dotted, string expected)
    {
        Assert.Equal(expected, NgxModelLayout.EncodePackedFolderName(dotted));
    }

    [Theory]
    [InlineData("310.999.0")]   // minor does not fit in a byte
    [InlineData("310.7.300")]   // patch does not fit in a byte
    [InlineData("garbage")]
    [InlineData("310")]         // too few components to encode
    [InlineData("")]
    [InlineData(null)]
    public void EncodePackedFolderName_RefusesUnencodableInput(string? dotted)
    {
        // Returning null (rather than a truncated folder name) matters: a silently wrong packed
        // folder would misfile a DLL where the driver never looks.
        Assert.Null(NgxModelLayout.EncodePackedFolderName(dotted));
    }

    [Theory]
    [InlineData(0, 256, 0)]
    [InlineData(0, 0, 256)]
    [InlineData(-1, 0, 0)]
    public void EncodePackedVersion_ThrowsOnOutOfRangeComponents(int major, int minor, int patch)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NgxModelLayout.EncodePackedVersion(major, minor, patch));
    }

    // ---- packed vs dotted discrimination --------------------------------------------------

    [Theory]
    [InlineData("20318080", true)]
    [InlineData("134144", true)]
    [InlineData("65536", true)]        // 1.0.0 — the floor
    [InlineData("65535", false)]       // below the floor: cannot encode a real major
    [InlineData("0", false)]
    [InlineData("7", false)]
    [InlineData("310.7.0.0", false)]   // dotted is not packed
    [InlineData(".dlss-backup-123", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void IsPackedVersionFolderName_DiscriminatesCorrectly(string folder, bool expected)
    {
        Assert.Equal(expected, NgxModelLayout.IsPackedVersionFolderName(folder));
    }

    // ---- THE REGRESSION: mixed-scheme ordering -------------------------------------------

    /// <summary>
    /// RED ARM for the v0.0.51 defect. Before the fix, OrderVersionFoldersNewestFirst padded a
    /// packed name to "20318080.0" and parsed it as Version(20318080, 0) — which dwarfs
    /// (310,10,0,0), so ANY packed folder always sorted newest no matter what it encoded. Decoding
    /// first makes 20318080 correctly rank as 310.7.128: newer than 310.7.0, older than 310.10.0.
    /// </summary>
    [Fact]
    public void OrderVersionFoldersNewestFirst_RanksPackedByItsDecodedValue()
    {
        var folders = new[]
        {
            @"C:\NGX\versions\310.7.0.0",
            @"C:\NGX\versions\20318080",   // == 310.7.128
            @"C:\NGX\versions\310.6.0.0",
            @"C:\NGX\versions\310.10.0.0",
            @"C:\NGX\versions\310.9.0.0",
        };

        var ordered = NgxScanner.OrderVersionFoldersNewestFirst(folders)
            .Select(Path.GetFileName)
            .ToArray();

        // 310.10 > 310.9 > 310.7.128 (packed) > 310.7.0 > 310.6
        Assert.Equal(
            new[] { "310.10.0.0", "310.9.0.0", "20318080", "310.7.0.0", "310.6.0.0" },
            ordered);

        // The specific pre-fix failure: the packed folder must NOT be first.
        Assert.NotEqual("20318080", ordered[0]);
    }

    [Fact]
    public void IsVersionFolderName_AcceptsBothSchemesAndRejectsOurBookkeeping()
    {
        Assert.True(NgxScanner.IsVersionFolderName("310.7.0.0"));
        Assert.True(NgxScanner.IsVersionFolderName("20318080"));

        Assert.False(NgxScanner.IsVersionFolderName(NgxScanner.BackupFolderPrefix + "20260101-120000"));
        Assert.False(NgxScanner.IsVersionFolderName("310.7.0.0" + NgxScanner.RestoreAsideSuffix));
        Assert.False(NgxScanner.IsVersionFolderName("not-a-version"));
        Assert.False(NgxScanner.IsVersionFolderName(""));
    }

    [Theory]
    [InlineData("20318080", "310.7.128")]
    [InlineData("134144", "2.12.0")]
    [InlineData("310.7.0.0", "310.7.0.0")]   // dotted passes through untouched
    [InlineData("", "")]
    public void DisplayVersionFolderName_DecodesPackedForDisplayOnly(string folder, string expected)
    {
        Assert.Equal(expected, NgxModelLayout.DisplayVersionFolderName(folder));
    }

    // ---- model-tree layout, byte-for-byte against the log --------------------------------

    /// <summary>
    /// These exact paths appear in the observed glom log. If this test drifts, we are no longer
    /// writing where the driver reads.
    /// </summary>
    [Theory]
    [InlineData("dlss", "20318080", @"C:\ProgramData\NVIDIA\NGX\Staging\models\dlss\versions\20318080\files")]
    [InlineData("dlssg", "20318080", @"C:\ProgramData\NVIDIA\NGX\Staging\models\dlssg\versions\20318080\files")]
    [InlineData("dlssd", "20318080", @"C:\ProgramData\NVIDIA\NGX\Staging\models\dlssd\versions\20318080\files")]
    [InlineData("deepdvc", "20317952", @"C:\ProgramData\NVIDIA\NGX\Staging\models\deepdvc\versions\20317952\files")]
    public void GetComponentFilesDir_MatchesObservedStagingPaths(string component, string packed, string expected)
    {
        var actual = NgxModelLayout.GetComponentFilesDir(
            @"C:\ProgramData\NVIDIA\NGX", component, packed, staging: true);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void GetComponentFilesDir_ProductionOmitsStagingSegment()
    {
        var actual = NgxModelLayout.GetComponentFilesDir(
            @"C:\ProgramData\NVIDIA\NGX", "dlss", "20318080", staging: false);

        Assert.Equal(@"C:\ProgramData\NVIDIA\NGX\models\dlss\versions\20318080\files", actual);
        Assert.DoesNotContain(@"\Staging\", actual);
    }

    /// <summary>
    /// The log writes BOTH generic app ids for every component, so both must be produced. The
    /// arch prefix 160 (Turing) is what both independent sources use for nvngx .bin payloads.
    /// </summary>
    [Fact]
    public void GetBinFileNames_ProducesBothObservedAppIds()
    {
        var names = NgxModelLayout.GetBinFileNames().ToArray();

        Assert.Equal(2, names.Length);
        Assert.Contains("160_E658703.bin", names);
        Assert.Contains("160_E658700.bin", names);
    }

    [Fact]
    public void ArchPrefixes_CoverEveryArchitectureInTheObservedLog()
    {
        // The log enumerated exactly these seven architectures for each sl_* component.
        var expected = new Dictionary<string, string>
        {
            ["Volta"] = "140",
            ["Turing"] = "160",
            ["Ampere"] = "170",
            ["Hopper"] = "180",
            ["Ada"] = "190",
            ["Blackwell"] = "1A0",
            ["Blackwell2"] = "1B0",
        };

        foreach (var (arch, prefix) in expected)
        {
            Assert.True(NgxModelLayout.ArchPrefixes.ContainsKey(arch), $"missing arch {arch}");
            Assert.Equal(prefix, NgxModelLayout.ArchPrefixes[arch]);
        }

        Assert.Equal(expected.Count, NgxModelLayout.ArchPrefixes.Count);
    }

    /// <summary>
    /// The component map must cover exactly the canonical DLL set. If a DLL is ever added to
    /// UpgradeService.NgxDllNames without a model-dir mapping, the import would silently skip it —
    /// the v0.0.43 "DeepDVC was missing from the copy list" defect class.
    /// </summary>
    [Fact]
    public void ComponentDirByDll_CoversExactlyTheCanonicalDllSet()
    {
        foreach (var dll in UpgradeService.NgxDllNames)
            Assert.True(NgxModelLayout.ComponentDirByDll.ContainsKey(dll), $"no model dir mapped for {dll}");

        Assert.Equal(UpgradeService.NgxDllNames.Length, NgxModelLayout.ComponentDirByDll.Count);
    }

    /// <summary>
    /// The observed log filed deepdvc under a different packed folder than the other three, because
    /// its DLL really was 310.7.0 while they were 310.7.128. Encoding must therefore be driven by
    /// each DLL's own version — one global version would misfile it.
    /// </summary>
    [Fact]
    public void PerComponentVersions_ProduceDifferentPackedFolders()
    {
        var srFolder = NgxModelLayout.EncodePackedFolderName("310.7.128.0");
        var dvcFolder = NgxModelLayout.EncodePackedFolderName("310.7.0.0");

        Assert.Equal("20318080", srFolder);
        Assert.Equal("20317952", dvcFolder);
        Assert.NotEqual(srFolder, dvcFolder);
    }
}
