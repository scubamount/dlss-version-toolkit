using System.Collections.ObjectModel;
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

    public MainViewModel(
        IScanService scanService,
        IUpgradeService upgradeService,
        IExportService exportService,
        ISettingsService settingsService,
        IBackupService backupService,
        IDlssDownloadService dlssDownloadService)
    {
        _scanService = scanService;
        _upgradeService = upgradeService;
        _exportService = exportService;
        _settingsService = settingsService;
        _backupService = backupService;
        _dlssDownloadService = dlssDownloadService;
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

        bool isAdmin = new System.Security.Principal.WindowsPrincipal(
            System.Security.Principal.WindowsIdentity.GetCurrent()
        ).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        if (!isAdmin)
        {
            MessageBox.Show("Administrator access is required to upgrade DLSS versions.\n\nPlease run the app as Administrator.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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

        bool isAdmin = new System.Security.Principal.WindowsPrincipal(
            System.Security.Principal.WindowsIdentity.GetCurrent()
        ).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        if (!isAdmin)
        {
            MessageBox.Show("Administrator access is required to sync DLSS versions.\n\nPlease run the app as Administrator.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

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
}