namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using DLSSVersionToolkit.Core.Models;

public interface IBackupService
{
	string? CreateBackup(string releaseFolderPath, string versionsParentPath);
	bool RestoreBackup(string backupPath, string releaseFolderPath);
	void CleanupOldBackups(string versionsParentPath, int keepCount = 10);
	/// <summary>Verifies that a backup directory exists, contains files, and optionally matches expected file count.</summary>
	bool VerifyBackup(string backupPath, int expectedFileCount = -1);
}

/// <summary>One on-disk NGX backup folder, parsed from its timestamped name.</summary>
public sealed record BackupEntry(string Path, DateTime Timestamp, int FileCount);

public class BackupService : IBackupService
{
    // Canonical backup-folder prefix and restore-aside suffix live on NgxScanner, because the
    // SCANNER is the component that must skip these folders. Duplicating the literal here is
    // how a producer and its skip-filter drift apart (rename the prefix, the scanner silently
    // starts listing backups as installed versions).
    private const string BackupPrefix = NgxScanner.BackupFolderPrefix;

    /// <summary>
    /// Enumerates backup folders under a versions parent, newest first. These folders have
    /// always been created by every sync (and restored automatically on rollback) — this just
    /// makes them visible so a user can roll back manually. Unparseable names are skipped.
    /// </summary>
    public static List<BackupEntry> ListBackups(string versionsParentPath)
    {
        var result = new List<BackupEntry>();
        if (string.IsNullOrEmpty(versionsParentPath) || !Directory.Exists(versionsParentPath))
            return result;
        try
        {
            foreach (var dir in Directory.GetDirectories(versionsParentPath, $"{BackupPrefix}*", SearchOption.TopDirectoryOnly))
            {
                var stamp = System.IO.Path.GetFileName(dir).Substring(BackupPrefix.Length);
                if (DateTime.TryParseExact(stamp, "yyyyMMdd-HHmmss", null,
                        System.Globalization.DateTimeStyles.None, out var ts))
                    result.Add(new BackupEntry(dir, ts, CountFiles(dir)));
            }
        }
        catch (Exception ex) { Debug.WriteLine($"ListBackups: error: {ex.Message}"); }
        return result.OrderByDescending(e => e.Timestamp).ToList();
    }
    /// <summary>
    /// Canonical NGX DLL set — derives from <see cref="UpgradeService.NgxDllNames"/> so this
    /// list can never silently diverge from what syncs (v0.0.43: DeepDVC was missing from a
    /// hardcoded sibling list for releases and never got backed up).
    /// </summary>
    private static readonly string[] DllNames = UpgradeService.NgxDllNames;

    public string? CreateBackup(string releaseFolderPath, string versionsParentPath)
    {
        if (string.IsNullOrEmpty(releaseFolderPath) || string.IsNullOrEmpty(versionsParentPath))
            return null;

        if (!Directory.Exists(releaseFolderPath))
            return null;

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var normalizedParent = Path.GetFullPath(versionsParentPath);

        bool isUnderProgramData = !string.IsNullOrEmpty(programData) &&
            normalizedParent.StartsWith(Path.Combine(programData, "NVIDIA"), StringComparison.OrdinalIgnoreCase);
        bool isUnderAppData = !string.IsNullOrEmpty(appData) &&
            normalizedParent.StartsWith(Path.Combine(appData, "NVIDIA"), StringComparison.OrdinalIgnoreCase);
        if (!isUnderProgramData && !isUnderAppData)
        {
            return null;
        }

        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var backupName = $"{BackupPrefix}{timestamp}";
        var backupPath = Path.Combine(versionsParentPath, backupName);

        if (Directory.Exists(backupPath))
            return null;

        var sourceFileCount = CountFiles(releaseFolderPath);
        if (sourceFileCount == 0)
            return null;

        try
        {
            var effectiveSource = EnsureLongPathSupport(releaseFolderPath);
            var effectiveDest = EnsureLongPathSupport(backupPath);

            CopyDirectory(effectiveSource, effectiveDest);

            var backupFileCount = CountFiles(backupPath);
            if (backupFileCount != sourceFileCount)
            {
                try { Directory.Delete(backupPath, true); }
                catch (Exception ex_c) { Debug.WriteLine($"CreateBackup: cleanup failed ({ex_c.Message})"); }
                return null;
            }

            return backupPath;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"CreateBackup: error: {ex.Message}");
            try { Directory.Delete(backupPath, true); }
            catch (Exception ex_c) { Debug.WriteLine($"CreateBackup: cleanup after error failed ({ex_c.Message})"); }
            return null;
        }
    }

    public bool RestoreBackup(string backupPath, string releaseFolderPath)
    {
        if (string.IsNullOrEmpty(backupPath) || string.IsNullOrEmpty(releaseFolderPath))
            return false;

        if (!Directory.Exists(backupPath))
            return false;

        // Swap discipline (same pattern as AppUpdateService): rename the current folder aside
        // FIRST, copy the backup into place, and only delete the old state once the new copy
        // verified. The old delete-then-copy could truncate the release folder mid-failure —
        // if the copy died, the current DLLs were already gone. Renaming is atomic and
        // reversible, so a failed restore always leaves the previous state recoverable.
        var asidePath = releaseFolderPath + NgxScanner.RestoreAsideSuffix;
        var failed = false;
        try
        {
            // 1. Move the current release folder aside (atomic, cheap).
            if (Directory.Exists(releaseFolderPath))
            {
                if (Directory.Exists(asidePath))
                    Directory.Delete(asidePath, true);
                Directory.Move(releaseFolderPath, asidePath);
            }

            // 2. Copy the backup into the release location.
            try
            {
                var effectiveSource = EnsureLongPathSupport(backupPath);
                var effectiveDest = EnsureLongPathSupport(releaseFolderPath);
                CopyDirectory(effectiveSource, effectiveDest);
            }
            catch
            {
                failed = true;
            }

            // 3. Verify the copy actually landed with the expected file count.
            var restoredFileCount = CountFiles(releaseFolderPath);
            if (failed || restoredFileCount == 0)
            {
                // Roll back: put the old state back.
                if (Directory.Exists(asidePath))
                {
                    if (Directory.Exists(releaseFolderPath))
                        Directory.Delete(releaseFolderPath, true);
                    Directory.Move(asidePath, releaseFolderPath);
                }
                return false;
            }

            // 4. Success — the old state can go.
            if (Directory.Exists(asidePath))
            {
                try { Directory.Delete(asidePath, true); }
                catch (Exception ex_d) { Debug.WriteLine($"RestoreBackup: cleanup of old state failed ({ex_d.Message})"); }
            }
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RestoreBackup: error: {ex.Message}");
            // Last-ditch rollback of the rename itself.
            try
            {
                if (Directory.Exists(asidePath) && !Directory.Exists(releaseFolderPath))
                    Directory.Move(asidePath, releaseFolderPath);
            }
            catch (Exception ex_r) { Debug.WriteLine($"RestoreBackup: rollback of rename failed ({ex_r.Message})"); }
            return false;
        }
    }

    public void CleanupOldBackups(string versionsParentPath, int keepCount = 10)
    {
        if (string.IsNullOrEmpty(versionsParentPath) || !Directory.Exists(versionsParentPath))
            return;

        try
        {
            var backups = Directory.GetDirectories(versionsParentPath, $"{BackupPrefix}*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(d => d)
                .Skip(keepCount)
                .ToList();

            foreach (var backup in backups)
            {
                try { Directory.Delete(backup, true); }
                catch (Exception ex_b) { Debug.WriteLine($"CleanupOldBackups: delete failed: {ex_b.Message}"); }
            }
        }
        catch (Exception ex) { Debug.WriteLine($"CleanupOldBackups: error: {ex.Message}"); }
    }

    private static int CountFiles(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories).Length;
        }
        catch (Exception ex) { Debug.WriteLine($"CountFiles: error: {ex.Message}"); return 0; }
    }

    private static string EnsureLongPathSupport(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            return path;
        if (path.Length >= 240)
            return @"\\?\" + path;
        return path;
    }

    private static void CopyDirectory(string source, string destination)
    {
        if (!Directory.Exists(destination))
            Directory.CreateDirectory(destination);

        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile, true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }
	public bool VerifyBackup(string backupPath, int expectedFileCount = -1)
	{
		return OperationGuard.VerifyBackupDirectory(backupPath, expectedFileCount);
	}
}