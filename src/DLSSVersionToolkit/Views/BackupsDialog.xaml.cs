using System.IO;
using System.Windows;
using DLSSVersionToolkit.Core.Services;
using DLSSVersionToolkit.Views;

namespace DLSSVersionToolkit;

public partial class BackupsDialog : Window
{
    private readonly IBackupService _backupService;
    private readonly string _versionsParentPath;

    /// <summary>
    /// One row in the list: a parsed NGX backup folder. <see cref="PathDisplay"/> is the parent
    /// folder name set by the code-behind so the DataGrid column can bind to something simple.
    /// </summary>
    public sealed record BackupRow(
        string Path, DateTime Timestamp, int FileCount, string PathDisplay);

    public BackupsDialog(IBackupService backupService, string versionsParentPath)
    {
        InitializeComponent();
        _backupService = backupService;
        _versionsParentPath = versionsParentPath;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var entries = BackupService.ListBackups(_versionsParentPath);
        foreach (var en in entries)
            BackupGrid.Items.Add(new BackupRow(en.Path, en.Timestamp, en.FileCount, en.Path));
        RestoreButton.IsEnabled = entries.Count > 0;
        if (entries.Count == 0)
            RestoreButton.ToolTip = "No NGX backups have been created yet. Run Update All once to make one.";
    }

    private void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (BackupGrid.SelectedItem is not BackupRow row)
        {
            ThemedMessageBox.Show("Select a backup first.", "NGX Backups", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Backups live directly under _versionsParentPath as siblings. The restore target is the
        // CURRENT release version folder under it (the highest-version-named directory) — the same
        // folder the sync created the backup from. There is no fixed "NGX_Release" subfolder name.
        var target = CurrentReleaseVersionFolder();
        if (target == null)
        {
            ThemedMessageBox.Show(
                "Could not find the current NGX Release version folder to restore into. " +
                "Run a scan first so the active version is known.",
                "NGX Backups", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = ThemedMessageBox.Show(
            $"Restore the NGX DLLs from the backup created {row.Timestamp:yyyy-MM-dd HH:mm}?\n\n" +
            $"This replaces the DLLs in the current release folder with those {row.FileCount} file(s).\n" +
            "Your current DLLs are backed up first, so nothing is permanently lost.",
            "Restore NGX Backups", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes)
            return;

        // Back up the current state before restoring, so this is reversible too.
        var safety = _backupService.CreateBackup(target, _versionsParentPath);
        if (safety == null || !_backupService.VerifyBackup(safety))
        {
            ThemedMessageBox.Show(
                "Could not create a safety backup of the current NGX DLLs, so the restore " +
                "was cancelled to avoid risking your current state.\n\n" +
                $"What to do: check free disk space and permissions on {_versionsParentPath}, " +
                "close any running game (it can hold the DLLs open), then try again.",
                "NGX Backups", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var restored = _backupService.RestoreBackup(row.Path, target);
        ThemedMessageBox.Show(
            restored
                ? "NGX DLLs restored successfully. Restart your game for the change to take effect."
                : "Restore failed. Your previous DLLs are still in place (see the safety backup).\n\n" +
                  "What to do: close any running game — it can hold an NGX DLL open — then restore again.",
            "NGX Backups",
            restored ? MessageBoxButton.OK : MessageBoxButton.OK,
            restored ? MessageBoxImage.Information : MessageBoxImage.Error);

        // Refresh the list — restore created a new safety backup.
        BackupGrid.Items.Clear();
        var refreshed = BackupService.ListBackups(_versionsParentPath);
        foreach (var en in refreshed)
            BackupGrid.Items.Add(new BackupRow(en.Path, en.Timestamp, en.FileCount, en.Path));
    }

    /// <summary>
    /// The active NGX Release version folder is the highest version-named directory directly
    /// under the versions parent (e.g. "310.7.0.0"). Version-named dirs are digits+'.'; backup
    /// folders (.dlss-backup-*) and anything else are excluded. Returns null if none found.
    /// </summary>
    private string? CurrentReleaseVersionFolder()
    {
        try
        {
            if (!Directory.Exists(_versionsParentPath))
                return null;
            // Same canonical predicate + numeric ordering the scanner uses. This used to filter
            // with a local regex and order with StringComparer.Ordinal, which put 310.9.0.0
            // above 310.10.0.0 — a restore would then overwrite the WRONG (older) folder and
            // leave the real newest untouched. That is the v0.0.43 lexical-sort defect class
            // reappearing in a sibling, which is why the rule now lives in exactly one place.
            return NgxScanner.OrderVersionFoldersNewestFirst(
                    Directory.GetDirectories(_versionsParentPath)
                        .Where(d => NgxScanner.IsVersionFolderName(Path.GetFileName(d))))
                .FirstOrDefault();
        }
        catch { return null; }
    }

    private void DoneButton_Click(object sender, RoutedEventArgs e) => Close();
}