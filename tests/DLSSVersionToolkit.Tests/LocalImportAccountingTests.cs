using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Pins the accounting of <see cref="LocalDllImportService"/> (v0.0.55).
///
/// Found by reconciling a real run report against itself. Andrew's screenshot showed
/// "Imported 4 component(s), 11 file(s) written" over a breakdown listing 2 files per component —
/// 4 x 2 = 8, not 11. The total was CORRECT (8 renamed .bin payloads plus 3 dlss_override copies
/// for dlssg/dlssd/deepdvc); the breakdown printed <c>BinFilesWritten</c> only, so three real
/// files were counted in the headline and named nowhere. Nothing was broken on disk and nothing
/// in CI could tell: no test read the two numbers together.
///
/// The lesson these tests encode: when a summary reports a total AND a breakdown, something must
/// assert they reconcile, or the breakdown drifts silently behind the total forever.
///
/// Auditing that led to two more defects, one of them live:
///   1. The manifest gate asked <c>BinFilesWritten &gt; 0</c>. A component whose .bin writes failed
///      verification but whose override copy landed has a file on disk and got NO manifest record —
///      the next Update All would overwrite it with nothing knowing an override existed. That is
///      precisely the failure the manifest was built to prevent, reachable through its own gate.
///   2. "Did this import land?" was asked four different ways across the service and its callers
///      (<c>Components.Count &gt; 0 &amp;&amp; FilesWritten.Count &gt; 0</c>,
///      <c>Success &amp;&amp; FilesWritten.Count &gt; 0</c>, <c>FilesWritten.Count == 0</c>,
///      <c>BinFilesWritten &gt; 0</c>). Four predicates for one question is four things to keep in
///      sync; they are now one (<c>Landed</c>).
///
/// These are behavior tests over real temp directories and real file copies — the DLL-version gate
/// means a fake PE with no version resource is correctly REJECTED, so the tests assert on that
/// rejection path plus the pure accounting properties. A test that reimplemented the counting would
/// be a tautology.
/// </summary>
public class LocalImportAccountingTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "dlssvt-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>
    /// A .bin count is not a file count. Before v0.0.55 the only per-component number was
    /// BinFilesWritten, so the override-tree copies were invisible in any breakdown built from it.
    /// </summary>
    [Fact]
    public void TotalFilesWritten_CountsOverrideCopiesNotJustBins()
    {
        var c = new ImportedComponent { BinFilesWritten = 2, OverrideFilesWritten = 1 };

        Assert.Equal(3, c.TotalFilesWritten);
        Assert.NotEqual(c.BinFilesWritten, c.TotalFilesWritten);
    }

    /// <summary>
    /// The exact shape of Andrew's screenshot: 4 components, 2 bins each, override copies for the
    /// three components that get one. Headline 11 must equal the sum of the breakdown.
    /// </summary>
    [Fact]
    public void ComponentTotals_ReconcileWithReportedFileCount()
    {
        var result = new LocalImportResult();

        // nvngx_dlss.dll: SR gets .bin payloads only — never an override-tree copy.
        result.Components.Add(new ImportedComponent
        {
            DllName = "nvngx_dlss.dll", Version = "310.7.128", PackedFolder = "20318080",
            BinFilesWritten = 2, OverrideFilesWritten = 0
        });
        foreach (var (dll, ver, packed) in new[]
                 {
                     ("nvngx_dlssg.dll", "310.7.128", "20318080"),
                     ("nvngx_dlssd.dll", "310.7.128", "20318080"),
                     ("nvngx_deepdvc.dll", "310.7.0", "20317952")
                 })
        {
            result.Components.Add(new ImportedComponent
            {
                DllName = dll, Version = ver, PackedFolder = packed,
                BinFilesWritten = 2, OverrideFilesWritten = 1
            });
        }

        for (var i = 0; i < 11; i++)
            result.FilesWritten.Add($@"C:\ProgramData\NVIDIA\NGX\file{i}");

        Assert.Equal(4, result.Components.Count);
        Assert.Equal(11, result.FilesWritten.Count);

        // The assertion the old report could not have passed: breakdown sums to headline.
        Assert.Equal(result.FilesWritten.Count, result.TotalFilesWrittenFromComponents);
    }

    /// <summary>
    /// Red-arms the reconciliation. Counting bins alone reproduces the 8-vs-11 mismatch, proving
    /// the test above fails against the pre-v0.0.55 breakdown rather than passing vacuously.
    /// </summary>
    [Fact]
    public void BinOnlyAccounting_UnderReportsTheRun()
    {
        var result = new LocalImportResult();
        result.Components.Add(new ImportedComponent { BinFilesWritten = 2, OverrideFilesWritten = 0 });
        result.Components.Add(new ImportedComponent { BinFilesWritten = 2, OverrideFilesWritten = 1 });
        result.Components.Add(new ImportedComponent { BinFilesWritten = 2, OverrideFilesWritten = 1 });
        result.Components.Add(new ImportedComponent { BinFilesWritten = 2, OverrideFilesWritten = 1 });
        for (var i = 0; i < 11; i++) result.FilesWritten.Add($"f{i}");

        var binOnly = result.Components.Sum(c => c.BinFilesWritten);

        Assert.Equal(8, binOnly);
        Assert.NotEqual(result.FilesWritten.Count, binOnly);
        Assert.Equal(result.FilesWritten.Count, result.TotalFilesWrittenFromComponents);
    }

    /// <summary>
    /// THE live bug. A component whose .bin writes failed verification but whose override copy
    /// landed has a file on disk. The old manifest gate (BinFilesWritten &gt; 0) said "no record",
    /// so the next Update All would overwrite a user's imported DLL with no trace it was ever
    /// asserted. Landed asks about files in EITHER tree.
    /// </summary>
    [Fact]
    public void Landed_IsTrueWhenOnlyTheOverrideCopySurvived()
    {
        var overrideOnly = new ImportedComponent { BinFilesWritten = 0, OverrideFilesWritten = 1 };

        Assert.True(overrideOnly.Landed, "a written override copy is a written file");
        Assert.False(overrideOnly.BinFilesWritten > 0, "the old gate would have skipped the manifest record here");
    }

    /// <summary>A component that wrote nothing is not an import, in either tree.</summary>
    [Fact]
    public void Landed_IsFalseWhenNothingWasWritten()
    {
        Assert.False(new ImportedComponent().Landed);
        Assert.False(new LocalImportResult().Landed);
    }

    /// <summary>
    /// Components only ever holds landed components, so the result-level predicate cannot disagree
    /// with the file list. This is the invariant that lets callers ask one question.
    /// </summary>
    [Fact]
    public void ResultLanded_AgreesWithFilesWritten()
    {
        var empty = new LocalImportResult();
        Assert.False(empty.Landed);
        Assert.Empty(empty.FilesWritten);

        var landed = new LocalImportResult();
        landed.Components.Add(new ImportedComponent { BinFilesWritten = 1 });
        landed.FilesWritten.Add("f");
        Assert.True(landed.Landed);
        Assert.Equal(landed.FilesWritten.Count, landed.TotalFilesWrittenFromComponents);
    }

    /// <summary>
    /// A folder with no NGX DLLs is a failure with a REASON, never a silent green. Real service,
    /// real temp dir — this is the path Update All's pre-flight check exists to catch earlier.
    /// </summary>
    [Fact]
    public void ImportFromFolder_EmptyFolder_FailsWithReason()
    {
        var src = NewTempDir();
        var ngx = NewTempDir();
        try
        {
            var result = new LocalDllImportService().ImportFromFolder(src, ngx, staging: true);

            Assert.False(result.Success);
            Assert.False(result.Landed);
            Assert.Empty(result.Components);
            Assert.False(string.IsNullOrEmpty(result.ErrorMessage));
        }
        finally
        {
            Directory.Delete(src, true);
            Directory.Delete(ngx, true);
        }
    }

    /// <summary>
    /// A missing source folder fails before any path resolution, and never reports success.
    /// </summary>
    [Fact]
    public void ImportFromFolder_MissingFolder_FailsWithoutWriting()
    {
        var missing = Path.Combine(Path.GetTempPath(), "dlssvt-nope-" + Guid.NewGuid().ToString("N"));

        var result = new LocalDllImportService().ImportFromFolder(missing, null, staging: true);

        Assert.False(result.Success);
        Assert.False(result.Landed);
        Assert.Empty(result.FilesWritten);
        Assert.Contains("not found", result.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A file that is not a real PE image is rejected by name-independent gates, and the reason is
    /// reported rather than swallowed. Pins the standing rule that DLL BYTES are the authority — a
    /// correct filename buys nothing.
    /// </summary>
    [Fact]
    public void ImportFromFolder_NonPeFile_IsSkippedWithReason()
    {
        var src = NewTempDir();
        var ngx = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(src, "nvngx_dlss.dll"), "not a PE image");

            var result = new LocalDllImportService().ImportFromFolder(src, ngx, staging: true);

            Assert.False(result.Success);
            Assert.False(result.Landed);
            Assert.Empty(result.FilesWritten);
            Assert.NotEmpty(result.Skipped);
        }
        finally
        {
            Directory.Delete(src, true);
            Directory.Delete(ngx, true);
        }
    }

    /// <summary>
    /// One rule, one predicate — enforced at the source, not by review.
    ///
    /// "Did the import land?" was rebuilt at four sites. Adding a correct branch to one copy makes
    /// the system less coherent, not more, so this fails if any caller reconstructs the test from
    /// Success/FilesWritten instead of asking Landed. Red-armed: against the v0.0.54 tree it flags
    /// MainViewModel.cs, which held two of the rebuilds.
    /// </summary>
    [Fact]
    public void ImportLandedPredicate_IsNotRebuiltByCallers()
    {
        var srcRoot = FindRepoSubdir("src");
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(srcRoot, "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            if (!text.Contains("FilesWritten", StringComparison.Ordinal))
                continue;

            // The service defines the predicate; everyone else consumes it.
            if (Path.GetFileName(file) == "LocalDllImportService.cs")
                continue;

            foreach (var bad in new[]
                     {
                         "Success && importResult.FilesWritten.Count > 0",
                         "Success && result.FilesWritten.Count > 0",
                         "importResult.FilesWritten.Count == 0",
                         "result.FilesWritten.Count == 0"
                     })
            {
                if (text.Contains(bad, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)} ({bad})");
            }
        }

        Assert.True(offenders.Count == 0,
            "\"did the import land\" rebuilt outside the result type in: " + string.Join(", ", offenders) +
            ". Use LocalImportResult.Landed.");
    }

    /// <summary>
    /// The import writes only inside allowlisted NGX write roots, which a normal user owns, so an
    /// access failure there is a held-open file — not missing elevation. v0.0.52 sent users to
    /// elevate for a problem elevation could never fix (the DriverStore is TrustedInstaller's);
    /// this keeps that misdiagnosis from creeping back into the message.
    ///
    /// Scoped by REGION, not by line. The first cut of this gate matched only lines that also said
    /// "import" or "NGX", and the v0.0.54 offender in MainViewModel read "If this is an access
    /// error, close any running game and run this app as Administrator." — no such word on it. A
    /// line-scoped filter for a method-scoped question finds one of two identical defects and
    /// reports clean. It now reads the whole import command body.
    /// </summary>
    [Fact]
    public void ImportMessages_DoNotAdviseElevation()
    {
        var srcRoot = FindRepoSubdir("src");
        var offenders = new List<string>();

        // Whole file: every write it performs is an NGX model write.
        var service = Path.Combine(srcRoot, "DLSSVersionToolkit.Core", "Services", "LocalDllImportService.cs");
        if (File.Exists(service))
            offenders.AddRange(ElevationAdviceIn(File.ReadAllLines(service), Path.GetFileName(service)));

        // The view model does many things; only the import command's body is in scope. The
        // whitelist and unlock paths write NVIDIA App data and DO legitimately need elevation.
        var vm = Path.Combine(srcRoot, "DLSSVersionToolkit", "ViewModels", "MainViewModel.cs");
        if (File.Exists(vm))
        {
            var lines = File.ReadAllLines(vm);
            var start = Array.FindIndex(lines, l => l.Contains("ImportLocalDllsAsync()", StringComparison.Ordinal));
            if (start >= 0)
            {
                // Run to the next command declaration — the end of this method's region.
                var end = Array.FindIndex(lines, start + 1, l => l.Contains("[RelayCommand]", StringComparison.Ordinal));
                if (end < 0) end = lines.Length;
                offenders.AddRange(ElevationAdviceIn(lines[start..end], "MainViewModel.ImportLocalDllsAsync"));
            }
        }

        Assert.True(offenders.Count == 0,
            "NGX import advises elevation, which cannot fix a write inside an allowlisted user-owned root: " +
            string.Join(" | ", offenders));
    }

    private static List<string> ElevationAdviceIn(IEnumerable<string> lines, string where)
    {
        var hits = new List<string>();
        foreach (var line in lines)
        {
            var trimmed = line.TrimStart();
            // Comments explain the history on purpose; only user-facing strings matter.
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;
            if (line.Contains("as Administrator", StringComparison.OrdinalIgnoreCase))
                hits.Add($"{where}: {trimmed}");
        }
        return hits;
    }

    /// <summary>
    /// Walks up from the test binary to the repo root and returns the named subdirectory.
    /// CI runs from bin/Release/net9.0, so this needs several hops.
    /// </summary>
    private static string FindRepoSubdir(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(dir.FullName, "tests")))
                return candidate;
        }
        throw new DirectoryNotFoundException($"Could not locate '{name}' from {AppContext.BaseDirectory}");
    }
}
