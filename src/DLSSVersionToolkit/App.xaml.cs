using System.IO;
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

    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DLSSVersionToolkit", "startup-crash.log");

    public App()
    {
        // Wire global exception handlers as early as possible — before Application_Startup —
        // so that ANY unhandled fault (managed or from a background task) is logged to disk
        // and surfaced to the user instead of the process vanishing with no window. The
        // 0.0.20 "double-click does nothing" reports were a startup exception with no handler.
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void LogCrash(string source, Exception? ex)
    {
        try
        {
            var dir = Path.GetDirectoryName(CrashLogPath);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var entry = $"[{DateTime.Now:O}] {source}: {ex}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(CrashLogPath, entry);
        }
        catch
        {
            // Never let logging itself crash the handler.
        }
    }

    private static void ShowCrashDialog(string source, Exception? ex)
    {
        try
        {
            MessageBox.Show(
                $"DLSS Version Toolkit hit an unexpected error and may not work correctly.\n\n" +
                $"Where: {source}\n" +
                $"Error: {ex?.Message}\n\n" +
                $"A log was written to:\n{CrashLogPath}",
                "DLSS Version Toolkit — Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // If even the dialog fails (e.g. no desktop), the on-disk log still captured it.
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DispatcherUnhandledException", e.Exception);
        ShowCrashDialog("UI thread", e.Exception);
        // Mark handled so a recoverable UI-thread fault doesn't silently kill the app.
        e.Handled = true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogCrash($"AppDomainUnhandledException (terminating={e.IsTerminating})", e.ExceptionObject as Exception);
        ShowCrashDialog("Background", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("UnobservedTaskException", e.Exception);
        e.SetObserved();
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        try
        {
            await StartupCoreAsync(e);
        }
        catch (Exception ex)
        {
            // Last line of defence: anything thrown synchronously during startup (service
            // construction, view-model construction, MainWindow construction) is logged and
            // shown rather than killing the process before a window appears.
            LogCrash("Application_Startup", ex);
            ShowCrashDialog("Startup", ex);
            Shutdown(1);
        }
    }

    private async System.Threading.Tasks.Task StartupCoreAsync(StartupEventArgs e)
    {
        // Single-instance enforcement — Global\ prefix ensures the mutex is visible
        // across all integrity levels, so elevated and non-elevated instances share it.
        const string mutexName = "Global\\DLSSVersionToolkit_SingleInstance";
        _mutex = new Mutex(false, mutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance is already running — try to bring it to front
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
        var streamlineDownloadService = new StreamlineDownloadService();
        var dlssIndicatorService = new DlssIndicatorService();
        var anWaveAutoService = new AnWaveAutoService();
        var whitelistService = new WhitelistService();
        var presetOverrideService = new PresetOverrideService();

        _mainViewModel = new MainViewModel(scanService, upgradeService, exportService, _settingsService, backupService, dlssDownloadService, streamlineDownloadService, anWaveAutoService, dlssIndicatorService, whitelistService, presetOverrideService);

        SetupTrayIcon();

        var mainWindow = new MainWindow { DataContext = _mainViewModel };
        mainWindow.Closing += MainWindow_Closing;

        MainWindow = mainWindow;
        mainWindow.Show();

        // Apply StartMinimized setting if configured
        var settings = await _settingsService.LoadAsync();
        if (settings.StartMinimized && settings.MinimizeToTray)
        {
            mainWindow.Hide();
            mainWindow.ShowInTaskbar = false;
            if (_trayIcon != null)
                _trayIcon.Visibility = Visibility.Visible;
        }

        // Only start background scheduler if user has opted in
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
        if (_settingsService == null)
        {
            ExitApplication();
            return;
        }
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