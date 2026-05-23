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

public class BackupService : IBackupService
{
    private const string BackupPrefix = ".dlss-backup-";
    private static readonly string[] DllNames = { "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll" };

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

        try
        {
            if (Directory.Exists(releaseFolderPath))
            {
                foreach (var file in Directory.GetFiles(releaseFolderPath, "*", SearchOption.AllDirectories))
                {
                    try { File.Delete(file); }
                    catch (Exception ex_f) { Debug.WriteLine($"RestoreBackup: file delete failed: {ex_f.Message}"); }
                }
                foreach (var dir in Directory.GetDirectories(releaseFolderPath, "*", SearchOption.AllDirectories))
                {
                    try { Directory.Delete(dir, true); }
                    catch (Exception ex_d) { Debug.WriteLine($"RestoreBackup: dir delete failed: {ex_d.Message}"); }
                }
            }

            var effectiveSource = EnsureLongPathSupport(backupPath);
            var effectiveDest = EnsureLongPathSupport(releaseFolderPath);

            CopyDirectory(effectiveSource, effectiveDest);

            var restoredFileCount = CountFiles(releaseFolderPath);
            return restoredFileCount > 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RestoreBackup: error: {ex.Message}");
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