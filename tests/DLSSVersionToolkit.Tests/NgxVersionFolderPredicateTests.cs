using System;
using System.IO;
using System.Linq;
using DLSSVersionToolkit.Core.Models;
using DLSSVersionToolkit.Core.Services;
using Xunit;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Pins the v0.0.48 audit fixes.
///
/// Both defects shipped in v0.0.47 and are instances of bug classes this repo has already
/// fixed once elsewhere — which is why the rules now live in ONE place (NgxScanner) and every
/// consumer routes through them:
///
///  A. The scanner enumerated every directory under versions\ with no filter, so the backup
///     folders it creates itself (.dlss-backup-*) and the transient restore-aside folder
///     (*.restoring) were parsed as installed NGX versions. Each backup holds a full copy of
///     the four NGX DLLs, so the INSTALLED VERSIONS grid gained a phantom row per backup —
///     each showing an OLD version — and the update comparison then read those stale rows.
///     (Stale-source-of-truth class, 7th instance.)
///
///  B. BackupsDialog chose the restore target with StringComparer.Ordinal, which orders
///     310.9.0.0 ABOVE 310.10.0.0. A restore would overwrite the wrong (older) folder and
///     leave the real newest untouched. (Lexical-version-sort class — same defect fixed in
///     DlssDownloadService.GetCachedVersions in v0.0.43.)
/// </summary>
public class NgxVersionFolderPredicateTests
{
    [Theory]
    [InlineData("310.7.0.0", true)]
    [InlineData("310.10.0.0", true)]
    [InlineData("311.2.0.0", true)]
    [InlineData("310.7", true)]
    [InlineData("310", true)]
    public void IsVersionFolderName_AcceptsRealVersionFolders(string name, bool expected)
        => Assert.Equal(expected, NgxScanner.IsVersionFolderName(name));

    [Theory]
    // Our own bookkeeping folders — these are the regression.
    [InlineData(".dlss-backup-20260815-120000")]
    [InlineData(".DLSS-BACKUP-20260815-120000")]
    [InlineData("310.7.0.0.restoring")]
    [InlineData("310.7.0.0.RESTORING")]
    // Non-version noise that must never become a grid row.
    [InlineData("NGX_Release")]
    [InlineData("Staging")]
    [InlineData("")]
    [InlineData("310.7.0.0-old")]
    public void IsVersionFolderName_RejectsBackupsAsideAndNoise(string name)
        => Assert.False(NgxScanner.IsVersionFolderName(name));

    [Fact]
    public void BackupPrefixAndAsideSuffix_AreRejectedByThePredicateThatGuardsThem()
    {
        // The producer (BackupService) and the skip-filter (NgxScanner) share these constants.
        // If someone changes the prefix, this fails rather than silently un-hiding backups.
        Assert.False(NgxScanner.IsVersionFolderName(NgxScanner.BackupFolderPrefix + "20260815-120000"));
        Assert.False(NgxScanner.IsVersionFolderName("310.7.0.0" + NgxScanner.RestoreAsideSuffix));
    }

    [Fact]
    public void OrderVersionFoldersNewestFirst_IsNumericNotLexical()
    {
        // The red arm: Ordinal string ordering puts 310.9.0.0 first. Numeric ordering is correct.
        var folders = new[]
        {
            Path.Combine("X", "310.7.0.0"),
            Path.Combine("X", "310.10.0.0"),
            Path.Combine("X", "310.9.0.0"),
        };

        var newest = NgxScanner.OrderVersionFoldersNewestFirst(folders).First();

        Assert.Equal("310.10.0.0", Path.GetFileName(newest));
        Assert.NotEqual("310.9.0.0", Path.GetFileName(newest));
    }

    [Fact]
    public void OrderVersionFoldersNewestFirst_HandlesShortAndUnparseableNames()
    {
        var folders = new[]
        {
            Path.Combine("X", "310"),
            Path.Combine("X", "311.0.0.0"),
            Path.Combine("X", "not-a-version"),
        };

        var ordered = NgxScanner.OrderVersionFoldersNewestFirst(folders).ToList();

        Assert.Equal("311.0.0.0", Path.GetFileName(ordered[0]));
        Assert.Equal("not-a-version", Path.GetFileName(ordered[^1]));
    }

    [Fact]
    public void Scan_SkipsBackupAndAsideFolders_AndStillReportsRealVersions()
    {
        // End-to-end proof on a real directory tree: a versions parent holding one real version
        // folder, one backup, and one restore-aside folder. Before the fix this returned 3 rows.
        var root = Path.Combine(Path.GetTempPath(), "dlssvt-scan-" + Guid.NewGuid().ToString("N")[..8]);
        var versions = Path.Combine(root, NgxScanner.ReleaseSubPath);
        try
        {
            var real = Path.Combine(versions, "310.7.0.0");
            var backup = Path.Combine(versions, NgxScanner.BackupFolderPrefix + "20260815-120000");
            var aside = Path.Combine(versions, "310.7.0.0" + NgxScanner.RestoreAsideSuffix);
            foreach (var d in new[] { real, backup, aside })
            {
                Directory.CreateDirectory(d);
                foreach (var dll in UpgradeService.NgxDllNames)
                    File.WriteAllBytes(Path.Combine(d, dll), new byte[] { 0x4D, 0x5A });
            }

            var scanner = new NgxScanner(new StubConfigParser());
            var results = scanner.Scan(root);

            Assert.Single(results);
            Assert.Equal("310.7.0.0", results[0].BuildID);
            Assert.DoesNotContain(results, r => r.BuildID.Contains("backup", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(results, r => r.BuildID.EndsWith(NgxScanner.RestoreAsideSuffix, StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { /* temp cleanup */ }
        }
    }

    [Fact]
    public void CleanupOldBackups_CountsOnlyBackupsTheUserCanSee()
    {
        // Retention used to glob the ".dlss-backup-*" prefix directly and order by name, while
        // ListBackups (what the dialog shows) skips names whose timestamp will not parse. A
        // stray unparseable folder therefore consumed a keepCount slot and pushed a real,
        // listed backup over the edge — deleting a backup the user could see while keeping
        // one they could not. Both paths now answer "what is a backup" with one function.
        var parent = Path.Combine(Path.GetTempPath(), "dlssvt-keep-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(parent);

            // Three real, listable backups (newest first: 03, 02, 01).
            foreach (var stamp in new[] { "20260815-120001", "20260815-120002", "20260815-120003" })
            {
                var d = Path.Combine(parent, NgxScanner.BackupFolderPrefix + stamp);
                Directory.CreateDirectory(d);
                File.WriteAllBytes(Path.Combine(d, "nvngx_dlss.dll"), new byte[] { 0x4D, 0x5A });
            }

            // A stray folder carrying the prefix but no parseable timestamp.
            var junk = Path.Combine(parent, NgxScanner.BackupFolderPrefix + "garbage");
            Directory.CreateDirectory(junk);

            var service = new BackupService();
            service.CleanupOldBackups(parent, keepCount: 2);

            var remaining = BackupService.ListBackups(parent);

            // Exactly the two newest listable backups survive.
            Assert.Equal(2, remaining.Count);
            Assert.Contains(remaining, b => b.Path.EndsWith("20260815-120003", StringComparison.Ordinal));
            Assert.Contains(remaining, b => b.Path.EndsWith("20260815-120002", StringComparison.Ordinal));
            Assert.DoesNotContain(remaining, b => b.Path.EndsWith("20260815-120001", StringComparison.Ordinal));

            // The unparseable folder is never counted and never deleted — it is not ours to judge.
            Assert.True(Directory.Exists(junk));
        }
        finally
        {
            try { Directory.Delete(parent, true); } catch { /* temp cleanup */ }
        }
    }

    private sealed class StubConfigParser : INgxConfigParser
    {
        public NgxConfigResult Parse(string versionFolderPath) => new()
        {
            DLSS = "310.7.0.0",
            FrameGen = "310.7.0.0",
            DLSSD = "310.7.0.0",
            DeepDVC = "310.7.0.0",
            IsReparsePoint = false,
        };
    }
}
