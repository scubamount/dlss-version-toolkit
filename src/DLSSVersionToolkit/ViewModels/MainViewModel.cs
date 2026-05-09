using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSVersionToolkit.Core.Models;
using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IScanService _scanService;
    private readonly IUpgradeService _upgradeService;
    private readonly IExportService _exportService;
    private readonly ISettingsService _settingsService;
    private readonly IBackupService _backupService;
    private readonly IDlssDownloadService _dlssDownloadService;
    private readonly IStreamlineDownloadService _streamlineDownloadService;
    private readonly IAnWaveAutoService _anWaveAutoService;
    private ScanResult? _lastScanResult;

    [ObservableProperty]
    private ObservableCollection<DLSSVersionEntry> _versions = new();

    [ObservableProperty]
    private ObservableCollection<Recommendation> _recommendations = new();

    [ObservableProperty]
    private string _lastScanTime = "Never";

    [ObservableProperty]
    private string _nextScanCountdown = "--";

    [ObservableProperty]
    private string _scanStatus = "Ready";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private bool _isDownloading;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private string _downloadStatus = "";

    [ObservableProperty]
    private string _cachedSdkVersion = "";

    [ObservableProperty]
    private bool _hasCachedSdk;

    [ObservableProperty]
    private string _anWaveDetectedPath = "";

    [ObservableProperty]
    private bool _isAnWaveDetected;

    [ObservableProperty]
    private string _anWaveInstalledPath = "";

    [ObservableProperty]
    private string _anWaveGlomVersion = "";

    [ObservableProperty]
    private string _anWaveDllVersion = "";

    [ObservableProperty]
    private bool _isAnWaveInstalled;

    [ObservableProperty]
    private bool _isSettingUpAnWave;

    [ObservableProperty]
    private bool _isUpdatingAll;

    [ObservableProperty]
    private string _cachedStreamlineVersion = "";

    [ObservableProperty]
    private bool _hasCachedStreamline;

    [ObservableProperty]
    private string _currentDlssVersion = "—";

    [ObservableProperty]
    private string _availableDlssVersion = "—";

    [ObservableProperty]
    private string _versionStatusMessage = "";

    [ObservableProperty]
    private bool _updateAvailable;

    public MainViewModel(
        IScanService scanService,
        IUpgradeService upgradeService,
        IExportService exportService,
        ISettingsService settingsService,
        IBackupService backupService,
        IDlssDownloadService dlssDownloadService,
        IStreamlineDownloadService streamlineDownloadService,
        IAnWaveAutoService anWaveAutoService)
    {
        _scanService = scanService;
        _upgradeService = upgradeService;
        _exportService = exportService;
        _settingsService = settingsService;
        _backupService = backupService;
        _dlssDownloadService = dlssDownloadService;
        _streamlineDownloadService = streamlineDownloadService;
        _anWaveAutoService = anWaveAutoService;
    }

    [RelayCommand]
    private async Task OneClickUpdateAllAsync()
    {
        if (IsScanning || IsSettingUpAnWave || IsUpdatingAll) return;

        IsUpdatingAll = true;
        ScanStatus = "Updating...";
        StatusMessage = "";
        DownloadStatus = "";

        try
        {
            // Step 1: Download latest DLSS SDK from NVIDIA if not cached
            var releases = await _dlssDownloadService.GetAvailableReleasesAsync();
            var latest = releases.FirstOrDefault();
            var needsDownload = latest != null &&
                _dlssDownloadService.GetCachedDownloadPath() == null;

            var sdkVersion = latest?.Version ?? "unknown";

            if (needsDownload)
            {
                DownloadStatus = "Downloading DLSS SDK v" + latest.Version + "...";
                var progress = new Progress<int>(pct => DownloadStatus = $"Downloading DLSS SDK... {pct}%");
                var path = await _dlssDownloadService.DownloadLatestAsync(progress);
                if (path == null)
                {
                    MessageBox.Show(
                        "Failed to download the latest DLSS SDK.\n\n" +
                        "What happened: The download from NVIDIA/DLSS on GitHub could not be completed.\n" +
                        "What to do: Check your internet connection and try again. If the problem persists, the GitHub API rate limit may have been reached — wait a few minutes.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                CachedSdkVersion = _dlssDownloadService.GetCachedSdkVersion() ?? "";
                sdkVersion = CachedSdkVersion;
                HasCachedSdk = true;
            }

            DownloadStatus = "Applying to NGX Release...";

            // Step 2: Sync cached SDK to NGX Release
            var settings = await _settingsService.LoadAsync();
            var ngxOp = await _dlssDownloadService.SyncFromCachedSdkAsync(null);
            if (ngxOp == null || ngxOp.Status == OperationStatus.Failed)
            {
                MessageBox.Show(
                    $"Failed to sync DLSS SDK v{sdkVersion} to NGX Release.\n\n" +
                    $"Error: {ngxOp?.ErrorMessage ?? "Unknown error"}\n\n" +
                    "What to do: Ensure the NGX directory exists at %ProgramData%\\NVIDIA\\NGX. Try running 'Sync from DLSS SDK' separately for more details.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (ngxOp.Status == OperationStatus.RolledBack)
            {
                MessageBox.Show(
                    $"DLSS SDK v{sdkVersion} sync to NGX failed and was rolled back.\n\n" +
                    $"Error: {ngxOp.ErrorMessage}\n" +
                    $"Backup preserved at: {ngxOp.BackupPath}\n\n" +
                    "What to do: Your previous NGX files have been restored. Check the error message above and try again.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DownloadStatus = "Applying to AnWave...";

            // Step 3: Apply to AnWave if installed (check all sources)
            var anWaveTarget = !string.IsNullOrEmpty(AnWaveInstalledPath) ? AnWaveInstalledPath
                : !string.IsNullOrEmpty(settings.AnWavePath) ? settings.AnWavePath
                : !string.IsNullOrEmpty(AnWaveDetectedPath) ? AnWaveDetectedPath : null;

            if (!string.IsNullOrEmpty(anWaveTarget) && Directory.Exists(anWaveTarget))
            {
                var anWaveOp = await _anWaveAutoService.AutoApplyAsync(AnWaveInstalledPath, settings.NgxBasePath, null);
                if (!anWaveOp.Success)
                {
                    var ngxFiles = string.Join("\n  • ", ngxOp.FilesCopied);
                    MessageBox.Show(
                        $"Partial update — NGX succeeded but AnWave failed.\n\n" +
                        $"✅ NGX Release: v{sdkVersion} applied ({ngxOp.FilesCopied.Count} files)\n" +
                        $"  {ngxFiles}\n\n" +
                        $"❌ AnWave: {anWaveOp.ErrorMessage}\n\n" +
                        "What to do: NGX is updated. To fix AnWave, try 'Apply to AnWave' separately or re-run 'Setup AnWave'.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    var ngxFiles = string.Join("\n  • ", ngxOp.FilesCopied);
                    var anWaveFiles = string.Join("\n  • ", anWaveOp.FilesCopied);
                    var appliedVer = anWaveOp.AppliedVersion ?? sdkVersion;
                    MessageBox.Show(
                        $"All updated successfully!\n\n" +
                        $"✅ NGX Release: v{sdkVersion} applied ({ngxOp.FilesCopied.Count} files)\n" +
                        $"  {ngxFiles}\n\n" +
                        $"✅ AnWave: v{appliedVer} applied ({anWaveOp.FilesCopied.Count} files)\n" +
                        $"  {anWaveFiles}\n\n" +
                        "DLSS Override is now globally active.\n" +
                        "Games using DLSS will use v" + appliedVer + ".",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                var ngxFiles = string.Join("\n  • ", ngxOp.FilesCopied);
                MessageBox.Show(
                    $"NGX Release updated to v{sdkVersion}.\n\n" +
                    $"Files copied ({ngxOp.FilesCopied.Count}):\n" +
                    $"  {ngxFiles}\n\n" +
                    "AnWave is not installed — the global DLSS Override is not active.\n\n" +
                    "What to do next: Click 'Setup AnWave' to complete the workflow and activate the global override for all games.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Update failed: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a network issue, try again. If it's a file access issue, ensure no other programs are using the NGX or AnWave directories.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsUpdatingAll = false;
            ScanStatus = "Ready";
            DownloadStatus = "";
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        ScanStatus = "Scanning...";
        StatusMessage = "";

        try
        {
            var result = await _scanService.ScanAllAsync();
            _lastScanResult = result;

            Versions.Clear();
            foreach (var entry in result.Sources)
            {
                Versions.Add(entry);
            }

            // Update version info after scan
            var ngxRelease = result.Sources.FirstOrDefault(s => s.Source == "NGX_Release");
            if (ngxRelease != null && ngxRelease.DLSS != "Unknown")
            {
                CurrentDlssVersion = ngxRelease.DLSS;
            }
            else
            {
                CurrentDlssVersion = "Not installed";
            }

            // Check available version from cached download
            var cachedVersion = _dlssDownloadService.GetCachedSdkVersion();
            if (!string.IsNullOrEmpty(cachedVersion))
            {
                AvailableDlssVersion = cachedVersion;
                UpdateAvailable = !string.IsNullOrEmpty(cachedVersion) &&
                    (ngxRelease == null || string.Compare(cachedVersion, ngxRelease.DLSS, StringComparison.OrdinalIgnoreCase) > 0);
                VersionStatusMessage = UpdateAvailable
                    ? $"v{cachedVersion} available (current: {CurrentDlssVersion})"
                    : "Already up to date";
            }
            else
            {
                AvailableDlssVersion = "—";
                UpdateAvailable = false;
                VersionStatusMessage = "";
            }
            // Check AnWave detection
            var anWaveEntry = result.Sources.FirstOrDefault(s => s.Source == "AnWave");
            if (anWaveEntry != null && !string.IsNullOrEmpty(anWaveEntry.Path))
            {
                AnWaveDetectedPath = anWaveEntry.Path;
                IsAnWaveDetected = true;
            }
            else
            {
                var detectedPath = DetectAnWavePath();
                if (!string.IsNullOrEmpty(detectedPath))
                {
                    AnWaveDetectedPath = detectedPath;
                    IsAnWaveDetected = true;
                }
                else
                {
                    AnWaveDetectedPath = "";
                    IsAnWaveDetected = false;
                }
            }

            Recommendations.Clear();
            foreach (var rec in result.Recommendations)
            {
                Recommendations.Add(rec);
            }

            LastScanTime = result.ScannedAt.ToString("yyyy-MM-dd HH:mm:ss");
            ScanStatus = result.HasErrors ? "Error" : result.HasWarnings ? "Warning" : "Ready";

            if (result.Warnings.Count > 0)
            {
                StatusMessage = string.Join("; ", result.Warnings);
            }

            if (result.Errors.Count > 0)
            {
                StatusMessage = string.Join("; ", result.Errors);
            }
        }
        catch (Exception ex)
        {
            ScanStatus = "Error";
            StatusMessage = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task UpgradeAsync()
    {
        StatusMessage = "";

        var ngxStaging = _lastScanResult?.Sources.FirstOrDefault(s => s.Source == "NGX_Staging");
        var ngxRelease = _lastScanResult?.Sources.FirstOrDefault(s => s.Source == "NGX_Release");
        var stagingVer = ngxStaging?.DLSS ?? "unknown";
        var releaseVer = ngxRelease?.DLSS ?? "unknown";

        var result = MessageBox.Show(
            $"Upgrade NGX Release from v{releaseVer} to v{stagingVer} (NGX Staging)?\n\n" +
            "A backup of the current NGX Release will be created before any changes.\n\n" +
            "Continue?",
            "Confirm Upgrade", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsScanning = true;
        ScanStatus = "Upgrading...";

        try
        {
            var settings = await _settingsService.LoadAsync();
            var operation = _upgradeService.UpgradeFromStaging(settings.NgxBasePath);

            switch (operation.Status)
            {
                case OperationStatus.Completed:
                    var files = string.Join("\n  • ", operation.FilesCopied);
                    MessageBox.Show(
                        $"Upgrade completed: v{releaseVer} → v{stagingVer}\n\n" +
                        $"Files copied ({operation.FilesCopied.Count}):\n" +
                        $"  {files}\n\n" +
                        $"Backup saved to:\n  {operation.BackupPath}\n\n" +
                        "What to do next: Your NGX Release is now updated. If you use AnWave, click 'Apply to AnWave' to keep it in sync.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case OperationStatus.Failed:
                    MessageBox.Show(
                        $"Upgrade from v{releaseVer} to v{stagingVer} failed.\n\n" +
                        $"Error: {operation.ErrorMessage}\n\n" +
                        "What to do: No files were changed. Check the error above and try again.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                case OperationStatus.RolledBack:
                    MessageBox.Show(
                        $"Upgrade from v{releaseVer} to v{stagingVer} failed and was rolled back.\n\n" +
                        $"Error: {operation.ErrorMessage}\n\n" +
                        $"Your previous NGX Release files have been restored.\n" +
                        $"Backup preserved at:\n  {operation.BackupPath}\n\n" +
                        "What to do: Check the error above. The backup folder contains the original files if needed.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                default:
                    MessageBox.Show(
                        $"Upgrade status: {operation.Status}\n\n" +
                        $"{operation.ErrorMessage}\n\n" +
                        "What to do: This is an unexpected status. Try scanning and upgrading again.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Upgrade failed: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a file access issue, ensure no other programs are using the NGX directory.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = "Ready";
        }
    }

    [RelayCommand]
    private async Task SyncFromStreamlineAsync()
    {
        if (IsScanning) return;

        // If Streamline path is configured, sync from it
        var settings = await _settingsService.LoadAsync();
        if (!string.IsNullOrEmpty(settings.StreamlinePath))
        {
            if (!Directory.Exists(settings.StreamlinePath))
            {
                MessageBox.Show(
                    "Streamline path is configured but the folder does not exist.\n\n" +
                    $"Configured path:\n {settings.StreamlinePath}\n\n" +
                    "What to do: Open Settings and update the Streamline SDK path to a valid folder, or clear it to auto-download from GitHub instead.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await SyncAsync("StreamlineSDK");
            return;
        }

        // No Streamline path configured — try auto-downloading from GitHub
        var result = MessageBox.Show(
            "No Streamline SDK path is configured.\n\n" +
            "Would you like to download the latest Streamline SDK from NVIDIA-RTX/Streamline on GitHub and sync it to NGX Release?\n\n" +
            "This will:\n" +
            " 1. Download the latest Streamline SDK from GitHub\n" +
            " 2. Extract and copy the DLLs to your NGX Release folder\n" +
            " 3. Create a backup of the current NGX Release files",
            "DLSS Version Toolkit", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsScanning = true;
        ScanStatus = "Downloading...";
        DownloadStatus = "Downloading latest Streamline SDK...";
        try
        {
            var progress = new Progress<int>(pct => DownloadStatus = $"Downloading Streamline SDK... {pct}%");
            var path = await _streamlineDownloadService.DownloadLatestAsync(progress);
            if (path == null)
            {
                MessageBox.Show(
                    "Failed to download the Streamline SDK.\n\n" +
                    "What to do: Check your internet connection and try again. If the problem persists, the GitHub API rate limit may have been reached — wait a few minutes.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            CachedStreamlineVersion = _streamlineDownloadService.GetCachedSdkVersion() ?? "";
            HasCachedStreamline = true;
            DownloadStatus = "Syncing to NGX...";
            var op = await _streamlineDownloadService.SyncFromCachedSdkAsync(null);
            if (op != null && op.Status == OperationStatus.Completed)
            {
                var files = string.Join("\n • ", op.FilesCopied);
                MessageBox.Show(
                    $"Streamline SDK v{CachedStreamlineVersion} downloaded and synced to NGX Release.\n\n" +
                    $"Files copied ({op.FilesCopied.Count}):\n" +
                    $" {files}\n\n" +
                    "What to do next: If you use AnWave, click 'Apply to AnWave' to keep it in sync.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                await ScanAsync();
            }
            else if (op?.Status == OperationStatus.RolledBack)
            {
                MessageBox.Show(
                    $"Streamline SDK v{CachedStreamlineVersion} was downloaded but sync to NGX failed and was rolled back.\n\n" +
                    $"Error: {op.ErrorMessage}\n\n" +
                    $"Backup preserved at:\n {op.BackupPath}\n\n" +
                    "What to do: Your previous NGX files have been restored. Check the error and try again.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                MessageBox.Show(
                    $"Streamline SDK v{CachedStreamlineVersion} was downloaded but sync to NGX failed.\n\n" +
                    $"Error: {op?.ErrorMessage ?? "Unknown error"}\n\n" +
                    "What to do: The SDK is cached and ready. Try 'Sync From Streamline SDK' again after scanning.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            IsScanning = false;
            ScanStatus = "Ready";
            DownloadStatus = "";
        }
    }

    [RelayCommand]
    private async Task SyncFromAnWaveAsync()
    {
        if (IsScanning) return;

        // Try: settings path → AnWaveAutoService installed path → detected path
        var settings = await _settingsService.LoadAsync();
        var targetPath = !string.IsNullOrEmpty(settings.AnWavePath) ? settings.AnWavePath
            : !string.IsNullOrEmpty(AnWaveInstalledPath) ? AnWaveInstalledPath
            : AnWaveDetectedPath;

        if (string.IsNullOrEmpty(targetPath))
        {
            MessageBox.Show(
                "AnWave is not set up — no install path found.\n\n" +
                "What to do: Click 'Setup AnWave' in the Advanced menu to automatically download and configure nvidiaDlssGlom with the latest DLSS DLLs.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(targetPath))
        {
            MessageBox.Show(
                "AnWave path does not exist.\n\n" +
                $"Path: {targetPath}\n\n" +
                "What to do: The folder may have been moved or deleted. Click 'Setup AnWave' to re-download and install it.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Temporarily set the settings path so SyncAsync uses it
        settings.AnWavePath = targetPath;
        await _settingsService.SaveAsync(settings);
        await SyncAsync("AnWave");
    }

    private async Task SyncAsync(string sourceType)
    {
        StatusMessage = "";

        var sourceEntry = _lastScanResult?.Sources.FirstOrDefault(s => s.Source == sourceType);
        var ngxRelease = _lastScanResult?.Sources.FirstOrDefault(s => s.Source == "NGX_Release");
        var sourceVer = sourceEntry?.DLSS ?? "unknown";
        var releaseVer = ngxRelease?.DLSS ?? "unknown";
        var displaySource = sourceType == "StreamlineSDK" ? "Streamline SDK" : sourceType;

        var result = MessageBox.Show(
            $"Sync DLSS from {displaySource} (v{sourceVer}) to NGX Release (v{releaseVer})?\n\n" +
            "A backup of the current NGX Release will be created before any changes.\n\n" +
            "Continue?",
            "Confirm Sync", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsScanning = true;
        ScanStatus = "Syncing...";

        try
        {
            var settings = await _settingsService.LoadAsync();
            string? sourcePath = sourceType == "StreamlineSDK" ? settings.StreamlinePath : settings.AnWavePath;

            if (string.IsNullOrEmpty(sourcePath) || !Directory.Exists(sourcePath))
            {
                MessageBox.Show(
                    $"{displaySource} path is not configured or the folder does not exist.\n\n" +
                    "What to do: Open Settings and set the correct path, or use 'Update All' to download the latest DLSS SDK instead.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var operation = _upgradeService.SyncToNGX(sourcePath, sourceType, settings.NgxBasePath);

            switch (operation.Status)
            {
                case OperationStatus.Completed:
                    var files = string.Join("\n  • ", operation.FilesCopied);
                    MessageBox.Show(
                        $"Sync completed: {displaySource} v{sourceVer} → NGX Release\n\n" +
                        $"Files copied ({operation.FilesCopied.Count}):\n" +
                        $"  {files}\n\n" +
                        $"Backup saved to:\n  {operation.BackupPath}\n\n" +
                        "What to do next: If you use AnWave, click 'Apply to AnWave' to keep it in sync.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case OperationStatus.Failed:
                    MessageBox.Show(
                        $"Sync from {displaySource} v{sourceVer} to NGX failed.\n\n" +
                        $"Error: {operation.ErrorMessage}\n\n" +
                        "What to do: No files were changed. Check the error above and try again.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                case OperationStatus.RolledBack:
                    MessageBox.Show(
                        $"Sync from {displaySource} v{sourceVer} to NGX failed and was rolled back.\n\n" +
                        $"Error: {operation.ErrorMessage}\n\n" +
                        $"Your previous NGX Release files have been restored.\n" +
                        $"Backup preserved at:\n  {operation.BackupPath}\n\n" +
                        "What to do: Check the error above. The backup folder contains the original files if needed.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                default:
                    MessageBox.Show(
                        $"Sync status: {operation.Status}\n\n" +
                        $"{operation.ErrorMessage}\n\n" +
                        "What to do: This is an unexpected status. Try scanning and syncing again.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Sync failed: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a file access issue, ensure no other programs are using the NGX directory.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = "Ready";
        }
    }

    [RelayCommand]
    private async Task SyncFromDlssSdkAsync()
    {
        if (IsScanning) return;

        if (!HasCachedSdk || string.IsNullOrEmpty(_dlssDownloadService.GetCachedDownloadPath()))
        {
            MessageBox.Show(
                "No DLSS SDK is cached.\n\n" +
                "What to do: Click 'Download Latest' in the Advanced menu or use 'Update All' to download the latest DLSS SDK from NVIDIA first.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ngxRelease = _lastScanResult?.Sources.FirstOrDefault(s => s.Source == "NGX_Release");
        var releaseVer = ngxRelease?.DLSS ?? "unknown";

        var result = MessageBox.Show(
            $"Sync DLSS SDK v{CachedSdkVersion} to NGX Release (currently v{releaseVer})?\n\n" +
            "A backup of the current NGX Release will be created before any changes.",
            "Confirm Sync", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsScanning = true;
        ScanStatus = "Syncing...";
        DownloadStatus = "";
        StatusMessage = "";

        try
        {
            var settings = await _settingsService.LoadAsync();
            var progress = new Progress<int>(pct => DownloadStatus = $"Syncing... {pct}%");
            var operation = await _dlssDownloadService.SyncFromCachedSdkAsync(progress);

            if (operation == null)
            {
                MessageBox.Show(
                    "No cached SDK found — the cached file may have been deleted.\n\n" +
                    "What to do: Click 'Download Latest' to re-download the DLSS SDK, then try again.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            switch (operation.Status)
            {
                case OperationStatus.Completed:
                    var files = string.Join("\n  • ", operation.FilesCopied);
                    MessageBox.Show(
                        $"DLSS SDK v{CachedSdkVersion} synced to NGX Release.\n\n" +
                        $"Previous version: v{releaseVer}\n" +
                        $"New version: v{CachedSdkVersion}\n\n" +
                        $"Files copied ({operation.FilesCopied.Count}):\n" +
                        $"  {files}\n\n" +
                        $"Backup saved to:\n  {operation.BackupPath}\n\n" +
                        "What to do next: If you use AnWave, click 'Apply to AnWave' to keep it in sync.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    await ScanAsync();
                    break;
                case OperationStatus.Failed:
                    MessageBox.Show(
                        $"Sync of DLSS SDK v{CachedSdkVersion} to NGX failed.\n\n" +
                        $"Error: {operation.ErrorMessage}\n\n" +
                        "What to do: No files were changed. Check the error above and try again.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                case OperationStatus.RolledBack:
                    MessageBox.Show(
                        $"Sync of DLSS SDK v{CachedSdkVersion} to NGX failed and was rolled back.\n\n" +
                        $"Error: {operation.ErrorMessage}\n\n" +
                        $"Your previous NGX Release files (v{releaseVer}) have been restored.\n" +
                        $"Backup preserved at:\n  {operation.BackupPath}\n\n" +
                        "What to do: Check the error above. The backup folder contains the original files if needed.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                default:
                    MessageBox.Show(
                        $"Unexpected sync status: {operation.Status}\n\n" +
                        $"{operation.ErrorMessage}\n\n" +
                        "What to do: Try scanning and syncing again.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Sync error: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a file access issue, ensure no other programs are using the NGX directory.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = "Ready";
        }
    }

    [RelayCommand]
    private async Task DownloadLatestAsync()
    {
        if (IsDownloading) return;

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatus = "Checking for latest release...";
        StatusMessage = "";

        try
        {
            var progress = new Progress<int>(pct =>
            {
                DownloadProgress = pct;
                DownloadStatus = $"Downloading... {pct}%";
            });

            var path = await _dlssDownloadService.DownloadLatestAsync(progress);

            if (path != null)
            {
                DownloadStatus = "Download complete.";
                var version = _dlssDownloadService.GetCachedSdkVersion() ?? "unknown";
                CachedSdkVersion = version;
                HasCachedSdk = !string.IsNullOrEmpty(CachedSdkVersion);

                var cacheInfo = _dlssDownloadService.GetCacheInfo();
                var sizeMb = cacheInfo.TotalBytes / (1024.0 * 1024.0);

                MessageBox.Show(
                    $"DLSS SDK v{version} downloaded successfully.\n\n" +
                    $"Saved to:\n  {path}\n\n" +
                    $"Cache: {cacheInfo.Count} file(s), {sizeMb:F1} MB total\n\n" +
                    "What to do next: Click 'Sync from DLSS SDK' to apply it to NGX Release, or use 'Update All' to sync to both NGX and AnWave.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                DownloadStatus = "";
                StatusMessage = "Failed to download. Check your internet connection.";
                MessageBox.Show(
                    "Failed to download the latest DLSS SDK.\n\n" +
                    "What to do: Check your internet connection and try again. If the problem persists, the GitHub API rate limit may have been reached — wait a few minutes.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            DownloadStatus = "";
            StatusMessage = $"Download error: {ex.Message}";
            MessageBox.Show(
                $"Download error: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a network issue, try again. If it's a disk issue, ensure you have free space in %APPDATA%\\DLSSVersionToolkit\\Downloads.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private async Task DownloadStreamlineAsync()
    {
        if (IsDownloading) return;

        IsDownloading = true;
        DownloadProgress = 0;
        DownloadStatus = "Checking for latest Streamline SDK release...";
        StatusMessage = "";

        try
        {
            var progress = new Progress<int>(pct =>
            {
                DownloadProgress = pct;
                DownloadStatus = $"Downloading Streamline SDK... {pct}%";
            });

            var path = await _streamlineDownloadService.DownloadLatestAsync(progress);

            if (path != null)
            {
                DownloadStatus = "Download complete.";
                var version = _streamlineDownloadService.GetCachedSdkVersion() ?? "unknown";
                CachedStreamlineVersion = version;
                HasCachedStreamline = !string.IsNullOrEmpty(CachedStreamlineVersion);

                var cacheInfo = _streamlineDownloadService.GetCacheInfo();
                var sizeMb = cacheInfo.TotalBytes / (1024.0 * 1024.0);

                MessageBox.Show(
                    $"Streamline SDK v{version} downloaded successfully.\n\n" +
                    $"Saved to:\n {path}\n\n" +
                    $"Cache: {cacheInfo.Count} file(s), {sizeMb:F1} MB total\n\n" +
                    "What to do next: Click 'Sync From Streamline SDK' to apply it to NGX Release, or use 'Update All' to sync the DLSS SDK to both NGX and AnWave.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                DownloadStatus = "";
                StatusMessage = "Failed to download Streamline SDK.";
                MessageBox.Show(
                    "Failed to download the latest Streamline SDK.\n\n" +
                    "What to do: Check your internet connection and try again. If the problem persists, the GitHub API rate limit may have been reached — wait a few minutes.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            DownloadStatus = "";
            StatusMessage = $"Download error: {ex.Message}";
            MessageBox.Show(
                $"Streamline SDK download error: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a network issue, try again. If it's a disk issue, ensure you have free space in %APPDATA%\\DLSSVersionToolkit\\StreamlineDownloads.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsDownloading = false;
        }
    }

    [RelayCommand]
    private void Export()
    {
        if (_lastScanResult == null || Versions.Count == 0)
        {
            MessageBox.Show(
                "No scan data to export.\n\n" +
                "What to do: Run a scan first by clicking the Scan button, then export the results.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv|JSON Files (*.json)|*.json",
            DefaultExt = ".csv",
            FileName = $"dlss-versions-{DateTime.Now:yyyyMMdd-HHmmss}"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                if (dialog.FilterIndex == 1)
                    _exportService.ExportToCsv(_lastScanResult, dialog.FileName);
                else
                    _exportService.ExportToJson(_lastScanResult, dialog.FileName);

                MessageBox.Show(
                    $"Exported {Versions.Count} source(s) to:\n  {dialog.FileName}\n\n" +
                    $"Format: {(dialog.FilterIndex == 1 ? "CSV" : "JSON")}",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Export failed: {ex.Message}\n\n" +
                    "What to do: Ensure the file is not open in another program and the destination folder is writable.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var settingsDialog = new SettingsDialog(_settingsService);
        if (settingsDialog.ShowDialog() == true)
        {
            StatusMessage = "Settings saved. Re-scan to apply changes.";
        }
    }

    [RelayCommand]
    private void ShowDashboard()
    {
        var mainWindow = App.Current.MainWindow as MainWindow;
        if (mainWindow != null)
        {
            mainWindow.Show();
            mainWindow.WindowState = WindowState.Normal;
            mainWindow.ShowInTaskbar = true;
            mainWindow.Activate();
        }
    }

    [RelayCommand]
    private void ExitApp()
    {
        Application.Current.Shutdown();
    }

    [RelayCommand]
    private async Task ApplyToAnWaveAsync()
    {
        if (IsScanning) return;

        var settings = await _settingsService.LoadAsync();
        var anWavePath = !string.IsNullOrEmpty(settings.AnWavePath) ? settings.AnWavePath
            : !string.IsNullOrEmpty(AnWaveInstalledPath) ? AnWaveInstalledPath
            : AnWaveDetectedPath;

        if (string.IsNullOrEmpty(anWavePath))
        {
            MessageBox.Show(
                "AnWave is not installed — no path found.\n\n" +
                "What to do: Click 'Setup AnWave' to automatically download and configure nvidiaDlssGlom with the latest DLSS DLLs.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Directory.Exists(anWavePath))
        {
            MessageBox.Show(
                "AnWave path does not exist — the folder may have been moved or deleted.\n\n" +
                $"Path: {anWavePath}\n\n" +
                "What to do: Click 'Setup AnWave' to re-download and install it.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ngxRelease = _lastScanResult?.Sources.FirstOrDefault(s => s.Source == "NGX_Release");
        var ngxVer = ngxRelease?.DLSS ?? "unknown";

        var result = MessageBox.Show(
            $"Apply NGX Release DLSS (v{ngxVer}) to AnWave?\n\n" +
            $"Target: {anWavePath}\n\n" +
            "This will copy the latest NGX Release DLLs to the AnWave folder.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsScanning = true;
        ScanStatus = "Applying...";
        StatusMessage = "";

        try
        {
            var operation = _upgradeService.ApplyToAnWave(anWavePath, settings.NgxBasePath);

            switch (operation.Status)
            {
                case OperationStatus.Completed:
                    var files = string.Join("\n  • ", operation.FilesCopied);
                    MessageBox.Show(
                        $"DLSS v{ngxVer} applied to AnWave.\n\n" +
                        $"Files copied ({operation.FilesCopied.Count}):\n" +
                        $"  {files}\n\n" +
                        "What to do next: The global DLSS Override should now be active. Launch a DLSS-enabled game to verify.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    await ScanAsync();
                    break;
                case OperationStatus.Failed:
                    MessageBox.Show(
                        $"Failed to apply DLSS to AnWave.\n\n" +
                        $"Error: {operation.ErrorMessage}\n\n" +
                        "What to do: Check the error above. Ensure the NGX Release folder contains valid DLLs and the AnWave folder is writable.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                default:
                    MessageBox.Show(
                        $"Unexpected status: {operation.Status}\n\n" +
                        $"{operation.ErrorMessage}\n\n" +
                        "What to do: Try scanning and applying again.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error applying to AnWave: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a file access issue, ensure no other programs are using the AnWave directory.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = "Ready";
        }
    }

    [RelayCommand]
    private async Task SyncDlssSdkToBothAsync()
    {
        if (!HasCachedSdk)
        {
            MessageBox.Show(
                "No DLSS SDK is cached.\n\n" +
                "What to do: Click 'Download Latest' in the Advanced menu or use 'Update All' to download the latest DLSS SDK from NVIDIA first.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (IsScanning) return;

        var ngxRelease = _lastScanResult?.Sources.FirstOrDefault(s => s.Source == "NGX_Release");
        var releaseVer = ngxRelease?.DLSS ?? "unknown";

        var result = MessageBox.Show(
            $"Sync DLSS SDK v{CachedSdkVersion} to NGX Release (currently v{releaseVer}) and then apply to AnWave?\n\n" +
            "This is a two-step process:\n" +
            $"  1. Sync v{CachedSdkVersion} to NGX Release (with backup)\n" +
            "  2. Apply the updated NGX DLLs to AnWave\n\n" +
            "Continue?",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsScanning = true;
        ScanStatus = "Syncing...";
        StatusMessage = "Step 1/2: Syncing to NGX...";
        DownloadStatus = "";

        try
        {
            var settings = await _settingsService.LoadAsync();

            // Step 1: Sync SDK to NGX
            var progress = new Progress<int>(pct => DownloadStatus = $"Syncing... {pct}%");
            var ngxxOperation = await _dlssDownloadService.SyncFromCachedSdkAsync(progress);

            if (ngxxOperation == null || ngxxOperation.Status == OperationStatus.Failed)
            {
                MessageBox.Show(
                    $"Step 1 failed — could not sync DLSS SDK v{CachedSdkVersion} to NGX Release.\n\n" +
                    $"Error: {ngxxOperation?.ErrorMessage ?? "Unknown error"}\n\n" +
                    "What to do: No files were changed. Check the error above and try again.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (ngxxOperation.Status == OperationStatus.RolledBack)
            {
                MessageBox.Show(
                    $"Step 1 failed — sync to NGX was rolled back.\n\n" +
                    $"Error: {ngxxOperation.ErrorMessage}\n\n" +
                    $"Backup preserved at:\n  {ngxxOperation.BackupPath}\n\n" +
                    "What to do: Your previous NGX files have been restored. Check the error and try again.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Step 2: Apply to AnWave if detected
            var anWaveTarget = !string.IsNullOrEmpty(settings.AnWavePath) ? settings.AnWavePath
                : !string.IsNullOrEmpty(AnWaveInstalledPath) ? AnWaveInstalledPath
                : AnWaveDetectedPath;

            if (!string.IsNullOrEmpty(anWaveTarget) && Directory.Exists(anWaveTarget))
            {
                StatusMessage = "Step 2/2: Applying to AnWave...";
                var anWavePath = !string.IsNullOrEmpty(settings.AnWavePath) ? settings.AnWavePath : AnWaveDetectedPath;
                var anWaveOp = _upgradeService.ApplyToAnWave(anWavePath, settings.NgxBasePath);

                if (anWaveOp.Status == OperationStatus.Completed)
                {
                    var ngxFiles = string.Join("\n  • ", ngxxOperation.FilesCopied);
                    var anWaveFiles = string.Join("\n  • ", anWaveOp.FilesCopied);
                    MessageBox.Show(
                        $"Both steps completed!\n\n" +
                        $"✅ Step 1 — NGX Release: v{CachedSdkVersion} applied ({ngxxOperation.FilesCopied.Count} files)\n" +
                        $"  {ngxFiles}\n\n" +
                        $"✅ Step 2 — AnWave: {anWaveOp.FilesCopied.Count} files updated\n" +
                        $"  {anWaveFiles}\n\n" +
                        $"NGX Backup: {ngxxOperation.BackupPath}\n\n" +
                        "What to do next: The global DLSS Override should now be active. Launch a DLSS-enabled game to verify.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    var ngxFiles = string.Join("\n  • ", ngxxOperation.FilesCopied);
                    MessageBox.Show(
                        $"Partial success — Step 1 completed but Step 2 failed.\n\n" +
                        $"✅ Step 1 — NGX Release: v{CachedSdkVersion} applied ({ngxxOperation.FilesCopied.Count} files)\n" +
                        $"  {ngxFiles}\n\n" +
                        $"❌ Step 2 — AnWave: {anWaveOp.ErrorMessage}\n\n" +
                        "What to do: NGX is updated. Try 'Apply to AnWave' separately or re-run 'Setup AnWave'.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                var ngxFiles = string.Join("\n  • ", ngxxOperation.FilesCopied);
                MessageBox.Show(
                    $"NGX sync complete — AnWave not found.\n\n" +
                    $"✅ NGX Release: v{CachedSdkVersion} applied ({ngxxOperation.FilesCopied.Count} files)\n" +
                    $"  {ngxFiles}\n\n" +
                    $"NGX Backup: {ngxxOperation.BackupPath}\n\n" +
                    "What to do next: Click 'Setup AnWave' to install it and activate the global DLSS Override.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a file access issue, ensure no other programs are using the NGX or AnWave directories.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = "Ready";
        }
    }

    private static string? DetectAnWavePath()
    {
        try
        {
            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");

            if (!Directory.Exists(downloads)) return null;

            var candidates = Directory.GetDirectories(downloads)
                .Where(d => Regex.IsMatch(
                    Path.GetFileName(d), "dlssglom|nvidiaDlssGlom|AnWave",
                    RegexOptions.IgnoreCase))
                .ToList();

            foreach (var candidate in candidates)
            {
                var exePath = Path.Combine(candidate, "nvidiaDlssGlom.exe");
                if (File.Exists(exePath)) return candidate;
            }
        }
        catch { }

        return null;
    }

    [RelayCommand]
    private async Task SetupAnWaveAsync()
    {
        if (IsSettingUpAnWave) return;

        IsSettingUpAnWave = true;
        ScanStatus = "Setting up AnWave...";
        StatusMessage = "";
        DownloadStatus = "Downloading nvidiaDlssGlom...";

        try
        {
            var progress = new Progress<int>(pct =>
            {
                if (pct < 30) DownloadStatus = $"Downloading nvidiaDlssGlom... {pct}%";
                else if (pct < 70) DownloadStatus = $"Downloading DLSS DLLs... {pct}%";
                else DownloadStatus = $"Activating override... {pct}%";
            });

            var result = await _anWaveAutoService.SetupAnWaveAsync(progress);

            if (result.Success)
            {
                IsAnWaveInstalled = true;
                AnWaveInstalledPath = result.InstalledPath ?? "";
                AnWaveGlomVersion = result.GlomVersion ?? "";
                AnWaveDllVersion = result.DllVersion ?? "";
                AnWaveDetectedPath = result.InstalledPath ?? "";
                IsAnWaveDetected = true;

                MessageBox.Show(
                    $"AnWave setup complete!\n\n" +
                    $"nvidiaDlssGlom v{result.GlomVersion} installed\n" +
                    $"DLSS version: v{result.DllVersion}\n\n" +
                    $"Location: {result.InstalledPath}\n\n" +
                    "DLSS Override has been activated globally.\n" +
                    "Games using DLSS should now use v" + result.DllVersion + ".\n\n" +
                    "What to do next: Launch a DLSS-enabled game to verify the override is working.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"AnWave setup failed.\n\n" +
                    $"Error: {result.ErrorMessage}\n\n" +
                    "What to do: Check the error above. If it's a network issue, try again. If it's a file access issue, ensure no other programs are using the AnWave directory.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"AnWave setup error: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a network issue, try again. If it's a file access issue, ensure the %APPDATA%\\DLSSVersionToolkit\\AnWave directory is writable.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsSettingUpAnWave = false;
            ScanStatus = "Ready";
            DownloadStatus = "";
        }
    }

    [RelayCommand]
    private async Task AutoApplyToAnWaveAsync()
    {
        if (IsScanning || IsSettingUpAnWave) return;

        var targetPath = AnWaveInstalledPath;
        if (string.IsNullOrEmpty(targetPath) || !Directory.Exists(targetPath))
        {
            MessageBox.Show(
                "AnWave is not installed yet.\n\n" +
                "What to do: Click 'Setup AnWave' first to download and configure nvidiaDlssGlom with the latest DLSS DLLs.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var ngxRelease = _lastScanResult?.Sources.FirstOrDefault(s => s.Source == "NGX_Release");
        var ngxVer = ngxRelease?.DLSS ?? "unknown";

        var result = MessageBox.Show(
            $"Apply NGX Release DLSS (v{ngxVer}) to AnWave?\n\n" +
            $"Target: {targetPath}\n\n" +
            "This will copy the latest NGX Release DLSS DLLs to AnWave\n" +
            "and activate the global override.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsScanning = true;
        ScanStatus = "Applying to AnWave...";
        DownloadStatus = "";

        try
        {
            var settings = await _settingsService.LoadAsync();
            var progress = new Progress<int>(pct => DownloadStatus = $"Applying... {pct}%");
            var applyResult = await _anWaveAutoService.AutoApplyAsync(targetPath, settings.NgxBasePath, progress);

            if (applyResult.Success)
            {
                AnWaveDllVersion = applyResult.AppliedVersion ?? AnWaveDllVersion;
                var appliedVer = applyResult.AppliedVersion ?? "unknown";
                var files = string.Join("\n  • ", applyResult.FilesCopied);
                MessageBox.Show(
                    $"DLSS v{appliedVer} applied to AnWave!\n\n" +
                    $"Files copied ({applyResult.FilesCopied.Count}):\n" +
                    $"  {files}\n\n" +
                    $"Config written: {(applyResult.ConfigWritten ? "Yes" : "No")}\n\n" +
                    "Global DLSS Override is now active.\n" +
                    "Games using DLSS should now use v" + appliedVer + ".\n\n" +
                    "What to do next: Launch a DLSS-enabled game to verify the override is working.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                await ScanAsync();
            }
            else
            {
                MessageBox.Show(
                    $"Failed to apply DLSS to AnWave.\n\n" +
                    $"Error: {applyResult.ErrorMessage}\n\n" +
                    "What to do: Check the error above. Ensure the NGX Release folder contains valid DLLs and the AnWave folder is writable.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error applying to AnWave: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a file access issue, ensure no other programs are using the AnWave directory.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = "Ready";
        }
    }
}
