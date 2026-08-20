namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;

/// <summary>
/// Shared pre-flight and verification helpers for file operations, network, and disk checks.
/// </summary>
public static class OperationGuard
{
    /// <summary>
    /// Checks if the system has network connectivity by attempting a TCP connection
    /// to GitHub's API endpoint (api.github.com:443). Returns true if reachable.
    /// </summary>
    public static bool IsNetworkAvailable(int timeoutMs = 3000)
    {
        try
        {
            // Fast check: is any network interface up?
            if (!NetworkInterface.GetIsNetworkAvailable())
                return false;

            // Verify actual connectivity by attempting TCP connect to GitHub API
            using var client = new TcpClient();
            var result = client.BeginConnect("api.github.com", 443, null, null);
            var success = result.AsyncWaitHandle.WaitOne(timeoutMs);
            if (!success)
                return false;

            client.EndConnect(result);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"IsNetworkAvailable: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if the specified directory exists and is writable by attempting
    /// to create and delete a temporary file.
    /// </summary>
    public static bool IsDirectoryWritable(string directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath))
            return false;

        if (!Directory.Exists(directoryPath))
            return false;

        try
        {
            var testFile = Path.Combine(directoryPath, $"_dlssvt_write_test_{Guid.NewGuid():N}.tmp");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"IsDirectoryWritable: {directoryPath} — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="candidatePath"/> is <paramref name="rootPath"/>
    /// or a descendant of it. Normalized, separator-aware comparison prevents lookalike
    /// siblings such as <c>NGX-evil</c> from matching an <c>NGX</c> allowlist entry.
    /// </summary>
    public static bool IsPathWithin(string candidatePath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(candidatePath) || string.IsNullOrWhiteSpace(rootPath))
            return false;

        try
        {
            var candidate = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidatePath));
            var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));

            return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) ||
                candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"IsPathWithin: {candidatePath} / {rootPath} — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if the specified directory has at least <paramref name="requiredBytes"/> free space.
    /// If the directory doesn't exist, walks up to the nearest existing parent.
    /// </summary>
    public static bool HasDiskSpace(string directoryPath, long requiredBytes)
    {
        try
        {
            // Find the nearest existing ancestor to check drive space
            var checkPath = directoryPath;
            while (!string.IsNullOrEmpty(checkPath) && !Directory.Exists(checkPath))
            {
                checkPath = Path.GetDirectoryName(checkPath);
            }

            if (string.IsNullOrEmpty(checkPath))
                return false;

            var drive = new DriveInfo(checkPath);
            if (!drive.IsReady)
                return false;

            return drive.AvailableFreeSpace >= requiredBytes;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HasDiskSpace: {directoryPath} — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verifies that a file at <paramref name="filePath"/> has a valid MZ/PE header
    /// (minimum 1024 bytes, first two bytes are 'M' and 'Z').
    /// </summary>
    public static bool VerifyDllSignature(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists || fi.Length < 1024)
                return false;

#pragma warning disable CA2022
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[2];
            _ = fs.Read(header, 0, 2);
#pragma warning restore CA2022

            return header[0] == 'M' && header[1] == 'Z';
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VerifyDllSignature: {filePath} — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verifies that a file exists at <paramref name="filePath"/> and has exactly
    /// <paramref name="expectedSize"/> bytes. If expectedSize is -1, only checks existence.
    /// </summary>
    public static bool VerifyFile(string filePath, long expectedSize = -1)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            if (expectedSize < 0)
                return true;

            var actualSize = new FileInfo(filePath).Length;
            return actualSize == expectedSize;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VerifyFile: {filePath} — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Verifies that a backup directory exists, contains at least one file,
    /// and optionally matches an expected file count.
    /// </summary>
    public static bool VerifyBackupDirectory(string backupPath, int expectedFileCount = -1)
    {
        try
        {
            if (string.IsNullOrEmpty(backupPath) || !Directory.Exists(backupPath))
                return false;

            var fileCount = Directory.GetFiles(backupPath, "*", SearchOption.AllDirectories).Length;
            if (fileCount == 0)
                return false;

            if (expectedFileCount >= 0 && fileCount != expectedFileCount)
                return false;

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"VerifyBackupDirectory: {backupPath} — {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Ensures a directory exists, creating it if necessary. Returns false if creation fails.
    /// </summary>
    public static bool EnsureDirectoryExists(string directoryPath)
    {
        try
        {
            if (string.IsNullOrEmpty(directoryPath))
                return false;

            if (!Directory.Exists(directoryPath))
                Directory.CreateDirectory(directoryPath);

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EnsureDirectoryExists: {directoryPath} — {ex.Message}");
            return false;
        }
    }
}
