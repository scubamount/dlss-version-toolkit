using System;
using System.IO;
using DLSSVersionToolkit.Core.Models;
using DLSSVersionToolkit.Core.Services;
using Xunit;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// v0.0.47: the run-report persistence and the backups listing. These pin that success
/// evidence now outlives the dialog (a report file is written and trimmed) and that
/// on-disk backup folders become discoverable/restorable instead of invisible internals.
/// </summary>
public class RunReportAndBackupsTests
{
    [Fact]
    public void Save_WritesJsonAndTrimsToKeepCount()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dlss-runs-{Guid.NewGuid():N}");
        try
        {
            // Write more than KeepCount; oldest must be trimmed away.
            for (var i = 0; i < RunReportStore.KeepCount + 3; i++)
            {
                var r = new UpdateRunReport
                {
                    StartedAt = new DateTime(2026, 8, 1).AddSeconds(i),
                    FinishedAt = new DateTime(2026, 8, 1).AddSeconds(i + 1),
                    AppVersion = "0.0.47"
                };
                r.Steps.Add(new UpdateRunStep { Name = "Whitelist", Status = "ok", Detail = "x" });
                RunReportStore.Save(r, dir);
            }
            var remaining = Directory.GetFiles(dir, "run-*.json");
            Assert.Equal(RunReportStore.KeepCount, remaining.Length);

            // And the newest survived.
            var newest = remaining.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).Last();
            var parsed = System.Text.Json.JsonSerializer.Deserialize<UpdateRunReport>(File.ReadAllText(newest));
            Assert.NotNull(parsed);
            Assert.Equal("0.0.47", parsed!.AppVersion);
            Assert.Single(parsed.Steps);
            Assert.Equal("Whitelist", parsed.Steps[0].Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void CleanupOldBackups_KeepsNewestN_DeletesRest_ReportsCount()
    {
        var versionsParent = Path.Combine(Path.GetTempPath(), $"dlss-retain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(versionsParent);
        try
        {
            // 13 real backups (oldest 20260801, newest 20260813) + one decoy that must survive
            // (not a backup per ListBackups, so retention must not count or touch it).
            for (var d = 1; d <= 13; d++)
                Directory.CreateDirectory(Path.Combine(versionsParent, $".dlss-backup-202608{d:00}-120000"));
            var decoy = Path.Combine(versionsParent, ".dlss-backup-garbage");
            Directory.CreateDirectory(decoy);

            var removed = new BackupService().CleanupOldBackups(versionsParent, keepCount: 10);

            Assert.Equal(3, removed);
            var left = BackupService.ListBackups(versionsParent);
            Assert.Equal(10, left.Count);
            // The three OLDEST went; the newest is untouched.
            Assert.DoesNotContain(left, e => e.Timestamp.Day <= 3);
            Assert.Contains(left, e => e.Timestamp.Day == 13);
            Assert.True(Directory.Exists(decoy), "unparseable folder is not a backup and must not be deleted");
        }
        finally
        {
            if (Directory.Exists(versionsParent)) Directory.Delete(versionsParent, true);
        }
    }

    /// <summary>
    /// Retention must be WIRED, not merely exist. CleanupOldBackups shipped with the backups
    /// feature and had zero callers through v0.65: every sync and restore-safety copy added a
    /// folder, nothing removed one. The producer (CreateBackup) is the single choke point all
    /// backup writers use, so it is where retention has to live — pinned here by source so a
    /// refactor cannot orphan it again.
    /// </summary>
    [Fact]
    public void BackupRetention_IsCalledByTheProducer()
    {
        var src = File.ReadAllText(Path.Combine(SiblingSweepTests.FindRepoSubdir("src"),
            "DLSSVersionToolkit.Core", "Services", "BackupService.cs"));

        var createStart = src.IndexOf("public string? CreateBackup(", StringComparison.Ordinal);
        var createEnd = src.IndexOf("public bool RestoreBackup(", StringComparison.Ordinal);
        Assert.True(createStart > 0 && createEnd > createStart, "CreateBackup/RestoreBackup layout changed — update this gate");

        var createBody = src.Substring(createStart, createEnd - createStart);
        Assert.Contains("CleanupOldBackups(versionsParentPath)", createBody);
        // Prune AFTER the new backup is verified, so the fresh copy is the newest and never pruned.
        Assert.True(createBody.IndexOf("CountFiles(backupPath)", StringComparison.Ordinal)
                  < createBody.IndexOf("CleanupOldBackups(versionsParentPath)", StringComparison.Ordinal),
            "retention must run after the new backup is verified");
    }

    [Fact]
    public void ListBackups_ReturnsNewestFirst_AndSkipsUnparseableNames()
    {
        var versionsParent = Path.Combine(Path.GetTempPath(), $"dlss-backups-{Guid.NewGuid():N}");
        Directory.CreateDirectory(versionsParent);
        try
        {
            // A real backup folder (timestamped) and a decoy that must be ignored.
            var real1 = Path.Combine(versionsParent, ".dlss-backup-20260801-120000");
            var real2 = Path.Combine(versionsParent, ".dlss-backup-20260802-090000");
            Directory.CreateDirectory(real1);
            Directory.CreateDirectory(real2);
            File.WriteAllText(Path.Combine(real1, "nvngx_dlss.dll"), "x");
            Directory.CreateDirectory(Path.Combine(versionsParent, ".dlss-backup-garbage"));

            var entries = BackupService.ListBackups(versionsParent);
            Assert.Equal(2, entries.Count);
            // Newest first.
            Assert.Contains(entries, e => e.Path == real2 && e.Timestamp == new DateTime(2026, 8, 2, 9, 0, 0));
            Assert.True(entries.FirstOrDefault(e => e.Path == real1)!.FileCount == 1);
        }
        finally
        {
            if (Directory.Exists(versionsParent)) Directory.Delete(versionsParent, true);
        }
    }

    [Fact]
    public void PresetOutcomeLabels_DocumentOnlyKnownLetters()
    {
        // Documented meanings render as prose; undocumented letters fall back to "Preset X".
        Assert.Equal("Preset E — older RR models (pre-310.7.128)", DlssPresetDisplay.GetRrDescription(DlssPreset.E));
        Assert.Equal("Preset F — recommended (DLSS 4.5 RR and newer)", DlssPresetDisplay.GetRrDescription(DlssPreset.F));
        Assert.Equal("Preset B — recommended (higher quality than A)", DlssPresetDisplay.GetFgDescription(DlssPreset.B));
        Assert.Equal("Preset K", DlssPresetDisplay.GetRrDescription(DlssPreset.K));   // no invented claim
        Assert.Equal("Preset J", DlssPresetDisplay.GetFgDescription(DlssPreset.J));    // no invented claim
        Assert.Equal("Default (no override)", DlssPresetDisplay.GetRrDescription(DlssPreset.Default));
    }
}