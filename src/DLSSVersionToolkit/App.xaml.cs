using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DLSSVersionToolkit.Core.Services;
using DLSSVersionToolkit.ViewModels;

namespace DLSSVersionToolkit;

public partial class App : Application
{
    private static Mutex? _mutex;
    private DispatcherTimer? _scanTimer;
    private Hardcodet.Wpf.TaskbarNotification.TaskbarIcon? _trayIcon;
    private MainViewModel? _mainViewModel;
    private ISettingsService? _settingsService;

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // Single-instance enforcement
        const string mutexName = "Global\\DLSSVersionToolkit_SingleInstance";
        _mutex = new Mutex(false, mutexName, out bool createdNew);

        if (!createdNew)
        {
            MessageBox.Show("DLSS Version Toolkit is already running.", "DLSS Version Toolkit",
                MessageBoxButton.OK, MessageBoxImage.Information);
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var processes = System.Diagnostics.Process.GetProcessesByName("DLSSVersionToolkit");
            foreach (var proc in processes)
            {
                if (proc.Id != currentProcess.Id && proc.MainWindowHandle != IntPtr.Zero)
                {
                    SetForegroundWindow(proc.MainWindowHandle);
                    break;
                }
            }
            Shutdown();
            return;
        }

        // Initialize services
        _settingsService = new SettingsService();
        var configParser = new NgxConfigParser();
        var ngxScanner = new NgxScanner(configParser);
        var globalScanner = new GlobalScanner();
        var streamlineScanner = new StreamlineScanner();
        var versionComparer = new VersionComparer();
        var backupService = new BackupService();
        var upgradeService = new UpgradeService(ngxScanner, backupService);
        var scanService = new ScanService(ngxScanner, globalScanner, streamlineScanner, versionComparer, _settingsService);
        var exportService = new ExportService();
        var dlssDownloadService = new DlssDownloadService();
        var anWaveAutoService = new AnWaveAutoService();

        _mainViewModel = new MainViewModel(scanService, upgradeService, exportService, _settingsService, backupService, dlssDownloadService, anWaveAutoService);

        SetupTrayIcon();

        var mainWindow = new MainWindow { DataContext = _mainViewModel };
        mainWindow.Closing += MainWindow_Closing;

        MainWindow = mainWindow;
        mainWindow.Show();

        // Only start background scheduler if user has opted in
        var settings = await _settingsService.LoadAsync();
        if (settings.AutoScanEnabled)
        {
            SetupScanScheduler(settings.ScanIntervalHours);
        }
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new Hardcodet.Wpf.TaskbarNotification.TaskbarIcon
        {
            ToolTipText = "DLSS Version Toolkit"
        };

        var contextMenu = new System.Windows.Controls.ContextMenu();

        var showItem = new System.Windows.Controls.MenuItem { Header = "Show Dashboard" };
        showItem.Click += (s, e) => ShowMainWindow();
        contextMenu.Items.Add(showItem);

        var checkNowItem = new System.Windows.Controls.MenuItem { Header = "Check Now" };
        checkNowItem.Click += (s, e) => _ = _mainViewModel?.ScanCommand.ExecuteAsync(null);
        contextMenu.Items.Add(checkNowItem);

        contextMenu.Items.Add(new System.Windows.Controls.Separator());

        var exitItem = new System.Windows.Controls.MenuItem { Header = "Exit" };
        exitItem.Click += (s, e) => ExitApplication();
        contextMenu.Items.Add(exitItem);

        _trayIcon.ContextMenu = contextMenu;
        _trayIcon.TrayMouseDoubleClick += (s, e) => ShowMainWindow();
    }

    private void SetupScanScheduler(int intervalHours)
    {
        _scanTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(intervalHours > 0 ? intervalHours : 4) };
        _scanTimer.Tick += async (s, e) =>
        {
            if (_mainViewModel != null)
                await _mainViewModel.ScanCommand.ExecuteAsync(null);
        };
        _scanTimer.Start();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settingsService ??= new SettingsService();
        var settings = _settingsService.GetCached();

        if (settings.MinimizeToTray)
        {
            e.Cancel = true;
            if (MainWindow != null)
            {
                MainWindow.Hide();
                MainWindow.ShowInTaskbar = false;
            }
            if (_trayIcon != null)
                _trayIcon.Visibility = Visibility.Visible;
        }
        else
        {
            ExitApplication();
        }
    }

    private void ShowMainWindow()
    {
        if (MainWindow != null)
        {
            MainWindow.Show();
            MainWindow.WindowState = WindowState.Normal;
            MainWindow.ShowInTaskbar = true;
            MainWindow.Activate();
        }
        if (_trayIcon != null)
            _trayIcon.Visibility = Visibility.Collapsed;
    }

    private void ExitApplication()
    {
        _scanTimer?.Stop();
        _trayIcon?.Dispose();
        Shutdown();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        _scanTimer?.Stop();
        _trayIcon?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}