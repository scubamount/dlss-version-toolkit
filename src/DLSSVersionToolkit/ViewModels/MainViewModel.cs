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
        IAnWaveAutoService anWaveAutoService)
    {
        _scanService = scanService;
        _upgradeService = upgradeService;
        _exportService = exportService;
        _settingsService = settingsService;
        _backupService = backupService;
        _dlssDownloadService = dlssDownloadService;
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

            if (needsDownload)
            {
                DownloadStatus = "Downloading DLSS SDK v" + latest.Version + "...";
                var progress = new Progress<int>(pct => DownloadStatus = $"Downloading DLSS SDK... {pct}%");
                var path = await _dlssDownloadService.DownloadLatestAsync(progress);
                if (path == null)
                {
                    MessageBox.Show("Failed to download the latest DLSS SDK.", "DLSS Version Toolkit",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                CachedSdkVersion = _dlssDownloadService.GetCachedSdkVersion() ?? "";
                HasCachedSdk = true;
            }

            DownloadStatus = "Applying to NGX Release...";

            // Step 2: Sync cached SDK to NGX Release
            var settings = await _settingsService.LoadAsync();
            var ngxxOp = await _dlssDownloadService.SyncFromCachedSdkAsync(null);
            if (ngxxOp == null || ngxxOp.Status == OperationStatus.Failed)
            {
                MessageBox.Show($"Failed to sync to NGX: {ngxxOp?.ErrorMessage ?? "Unknown error"}",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DownloadStatus = "Applying to AnWave...";

            // Step 3: Apply to AnWave if installed
            if (IsAnWaveInstalled && !string.IsNullOrEmpty(AnWaveInstalledPath))
            {
                var anWaveOp = await _anWaveAutoService.AutoApplyAsync(AnWaveInstalledPath, settings.NgxBasePath, null);
                if (!anWaveOp.Success)
                {
                    MessageBox.Show($"NGX sync done ({ngxxOp.FilesCopied.Count} files).\nAnWave update failed: {anWaveOp.ErrorMessage}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"All updated!\nNGX: {ngxxOp.FilesCopied.Count} files\nAnWave: {anWaveOp.FilesCopied.Count} files\n\nDLSS Override is now globally active.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else
            {
                MessageBox.Show($"NGX Release updated.\n{ngxxOp.FilesCopied.Count} files copied.\n\nAnWave not installed — run 'Setup AnWave' to complete the workflow.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Update failed: {ex.Message}", "DLSS Version Toolkit",
                MessageBoxButton.OK, MessageBoxImage.Error);
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

        

        var result = MessageBox.Show("This will upgrade NGX Release to the latest Staging version.\nA backup will be created before any changes.\n\nContinue?",
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
                    MessageBox.Show($"Upgrade completed successfully.\nFiles copied: {operation.FilesCopied.Count}\nBackup: {operation.BackupPath}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case OperationStatus.Failed:
                    MessageBox.Show($"Upgrade failed: {operation.ErrorMessage}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                case OperationStatus.RolledBack:
                    MessageBox.Show($"Upgrade failed and was rolled back: {operation.ErrorMessage}\nBackup available at: {operation.BackupPath}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                default:
                    MessageBox.Show($"Upgrade status: {operation.Status}\n{operation.ErrorMessage}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Upgrade failed: {ex.Message}",
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
        await SyncAsync("StreamlineSDK");
    }

    [RelayCommand]
    private async Task SyncFromAnWaveAsync()
    {
        await SyncAsync("AnWave");
    }

    private async Task SyncAsync(string sourceType)
    {
        StatusMessage = "";

        var result = MessageBox.Show($"This will sync DLSS versions from {sourceType} to NGX Release.\nA backup will be created before any changes.\n\nContinue?",
            "Confirm Sync", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsScanning = true;
        ScanStatus = "Syncing...";

        try
        {
            var settings = await _settingsService.LoadAsync();
            string? sourcePath = sourceType == "StreamlineSDK" ? settings.StreamlinePath : settings.AnWavePath;

            if (string.IsNullOrEmpty(sourcePath))
            {
                MessageBox.Show($"{sourceType} path is not configured. Please set it in Settings.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var operation = _upgradeService.SyncToNGX(sourcePath, sourceType, settings.NgxBasePath);

            switch (operation.Status)
            {
                case OperationStatus.Completed:
                    MessageBox.Show($"Sync completed successfully.\nFiles copied: {operation.FilesCopied.Count}\nBackup: {operation.BackupPath}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
                case OperationStatus.Failed:
                    MessageBox.Show($"Sync failed: {operation.ErrorMessage}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                case OperationStatus.RolledBack:
                    MessageBox.Show($"Sync failed and was rolled back: {operation.ErrorMessage}\nBackup available at: {operation.BackupPath}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                default:
                    MessageBox.Show($"Sync status: {operation.Status}\n{operation.ErrorMessage}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    break;
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Sync failed: {ex.Message}",
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

        var result = MessageBox.Show($"Sync DLSS SDK {CachedSdkVersion} to NGX Release?\nA backup will be created first.",
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
                MessageBox.Show("No cached SDK found. Please download first.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            switch (operation.Status)
            {
                case OperationStatus.Completed:
                    MessageBox.Show($"Sync completed.\nFiles copied: {operation.FilesCopied.Count}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    await ScanAsync();
                    break;
                case OperationStatus.Failed:
                    MessageBox.Show($"Sync failed: {operation.ErrorMessage}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                case OperationStatus.RolledBack:
                    MessageBox.Show($"Sync failed and rolled back: {operation.ErrorMessage}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                default:
                    MessageBox.Show($"Unexpected status: {operation.Status}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Sync error: {ex.Message}",
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
                StatusMessage = $"DLSS SDK downloaded to:\n{path}";
                CachedSdkVersion = _dlssDownloadService.GetCachedSdkVersion() ?? "";
                HasCachedSdk = !string.IsNullOrEmpty(CachedSdkVersion);
                MessageBox.Show($"DLSS SDK downloaded successfully.\n\nSaved to:\n{path}",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                DownloadStatus = "";
                StatusMessage = "Failed to download. Check your internet connection.";
                MessageBox.Show("Failed to download the latest DLSS SDK.\nCheck your internet connection and try again.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            DownloadStatus = "";
            StatusMessage = $"Download error: {ex.Message}";
            MessageBox.Show($"Download error: {ex.Message}",
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
            MessageBox.Show("No data to export. Please scan first.",
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

                MessageBox.Show($"Exported to:\n{dialog.FileName}",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}",
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

        var anWavePath = AnWaveDetectedPath;
        if (string.IsNullOrEmpty(anWavePath))
        {
            MessageBox.Show("AnWave not detected. Please ensure AnWave is installed and scanned.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show($"Apply current NGX Release DLSS to AnWave?\nSource: {anWavePath}\n\nThis will copy the latest NGX Release DLLs to AnWave.",
            "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        IsScanning = true;
        ScanStatus = "Applying...";
        StatusMessage = "";

        try
        {
            var settings = await _settingsService.LoadAsync();
            var operation = _upgradeService.ApplyToAnWave(anWavePath, settings.NgxBasePath);

            switch (operation.Status)
            {
                case OperationStatus.Completed:
                    MessageBox.Show($"DLSS applied to AnWave.\nFiles copied: {operation.FilesCopied.Count}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                    await ScanAsync();
                    break;
                case OperationStatus.Failed:
                    MessageBox.Show($"Failed: {operation.ErrorMessage}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
                default:
                    MessageBox.Show($"Status: {operation.Status}\n{operation.ErrorMessage}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                    break;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}",
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
            MessageBox.Show("No cached DLSS SDK. Please download first.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (IsScanning) return;

        var result = MessageBox.Show($"Sync DLSS SDK {CachedSdkVersion} to NGX Release and then apply to AnWave?\n\nThis is a two-step process:\n1. Sync to NGX (with backup)\n2. Apply to AnWave",
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
                MessageBox.Show($"Step 1 failed: {ngxxOperation?.ErrorMessage ?? "Unknown error"}",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Step 2: Apply to AnWave if detected
            if (IsAnWaveDetected && !string.IsNullOrEmpty(AnWaveDetectedPath))
            {
                StatusMessage = "Step 2/2: Applying to AnWave...";
                var anWavePath = !string.IsNullOrEmpty(settings.AnWavePath) ? settings.AnWavePath : AnWaveDetectedPath;
                var anWaveOp = _upgradeService.ApplyToAnWave(anWavePath, settings.NgxBasePath);

                if (anWaveOp.Status == OperationStatus.Completed)
                {
                    MessageBox.Show($"Done!\nNGX: {ngxxOperation.FilesCopied.Count} files\nAnWave: {anWaveOp.FilesCopied.Count} files",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Step 2 partial failure: {anWaveOp.ErrorMessage}",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                MessageBox.Show($"NGX sync complete.\nFiles copied: {ngxxOperation.FilesCopied.Count}",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}",
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
                    $"nvidiaDlssGlom v{result.GlomVersion} installed.\n" +
                    $"DLSS version: {result.DllVersion}\n\n" +
                    $"Location: {result.InstalledPath}\n\n" +
                    $"DLSS Override has been activated globally.\n" +
                    $"Games using DLSS should now use the latest version.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"AnWave setup failed:\n{result.ErrorMessage}",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}",
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
            MessageBox.Show("AnWave is not installed yet. Please click 'Setup AnWave' first.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var result = MessageBox.Show(
            $"Apply current NGX Release DLLs to AnWave?\n\n" +
            $"Target: {targetPath}\n\n" +
            $"This will copy the latest NGX Release DLSS DLLs to AnWave\n" +
            $"and activate the global override.",
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
                MessageBox.Show(
                    $"DLSS applied to AnWave!\n\n" +
                    $"Files copied: {string.Join(", ", applyResult.FilesCopied)}\n" +
                    $"Version: {applyResult.AppliedVersion ?? "unknown"}\n\n" +
                    $"Global DLSS Override is now active.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
                await ScanAsync();
            }
            else
            {
                MessageBox.Show($"Failed: {applyResult.ErrorMessage}",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsScanning = false;
            ScanStatus = "Ready";
        }
    }
}