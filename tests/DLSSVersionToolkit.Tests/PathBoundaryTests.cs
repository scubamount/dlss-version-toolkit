using DLSSVersionToolkit.Core.Models;

namespace DLSSVersionToolkit.Tests;

public class PathBoundaryTests
{
    [Fact]
    public void IsPathWithin_AllowsRootAndDescendantsButRejectsPrefixLookalikes()
    {
        var root = Path.Combine(Path.GetTempPath(), "dlssvt-path-boundary", "NVIDIA", "NGX");

        Assert.True(Core.Services.OperationGuard.IsPathWithin(root, root));
        Assert.True(Core.Services.OperationGuard.IsPathWithin(Path.Combine(root, "versions", "310.10.0.0"), root));
        Assert.False(Core.Services.OperationGuard.IsPathWithin(root + "-lookalike", root));
        Assert.False(Core.Services.OperationGuard.IsPathWithin(root + "-lookalike", root + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void UpgradeFromStaging_PrefixLookalikePath_ReturnsFailed()
    {
        var allowedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NVIDIA", "NGX");
        var lookalikePath = allowedPath + "-lookalike";
        var service = new Core.Services.UpgradeService(
            new EmptyNgxScanner(),
            new UnusedBackupService(),
            new Core.Services.VersionComparer());

        var result = service.UpgradeFromStaging(lookalikePath);

        Assert.Equal(OperationStatus.Failed, result.Status);
        Assert.Contains("not in allowed list", result.ErrorMessage);
    }

    private sealed class EmptyNgxScanner : Core.Services.INgxScanner
    {
        public List<DLSSVersionEntry> Scan(string ngxBasePath) => [];
        public List<DLSSVersionEntry> Scan(string ngxBasePath, List<string>? errors) => Scan(ngxBasePath);
    }

    private sealed class UnusedBackupService : Core.Services.IBackupService
    {
        public string? CreateBackup(string releaseFolderPath, string versionsParentPath) =>
            throw new InvalidOperationException("Backup should not be attempted for a rejected path.");

        public bool RestoreBackup(string backupPath, string releaseFolderPath) =>
            throw new InvalidOperationException("Restore should not be attempted for a rejected path.");

        public void CleanupOldBackups(string versionsParentPath, int keepCount = 10) =>
            throw new InvalidOperationException("Cleanup should not be attempted for a rejected path.");

        public bool VerifyBackup(string backupPath, int expectedFileCount = -1) =>
            throw new InvalidOperationException("Verification should not be attempted for a rejected path.");
    }
}
