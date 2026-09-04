using System.Windows;
using Microsoft.Win32;
using DLSSVersionToolkit.Core.Services;
using DLSSVersionToolkit.Core.Models;

namespace DLSSVersionToolkit;

public partial class SettingsDialog : Window
{
    private readonly ISettingsService _settingsService;
    private AppSettings _settings;

    public SettingsDialog(ISettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _settings = new AppSettings();
        LoadSettings();
    }

    private async void LoadSettings()
    {
        _settings = await _settingsService.LoadAsync();

        NgxPathTextBox.Text = _settings.NgxBasePath;
        AnWavePathTextBox.Text = _settings.AnWavePath;
        StreamlinePathTextBox.Text = _settings.StreamlinePath;
        StartMinimizedCheckBox.IsChecked = _settings.StartMinimized;
        AutoScanCheckBox.IsChecked = _settings.AutoScanEnabled;
        MinimizeToTrayCheckBox.IsChecked = _settings.MinimizeToTray;
        NotifyOnNewVersionCheckBox.IsChecked = _settings.NotifyOnNewVersion;
        CheckForAppUpdatesCheckBox.IsChecked = _settings.CheckForAppUpdates;
        IncludePreReleaseCheckBox.IsChecked = _settings.IncludePreReleaseChannel;
        AllowOtaDownloadsCheckBox.IsChecked = _settings.AllowOtaPayloadDownloads;
        OtaRedistributionAcceptedCheckBox.IsChecked = _settings.OtaRedistributionAccepted;
    }

    private void BrowseNgxPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select NGX Base Path",
            InitialDirectory = NgxPathTextBox.Text
        };
        if (dialog.ShowDialog() == true)
        {
            NgxPathTextBox.Text = dialog.FolderName;
        }
    }

    private void BrowseAnWavePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select AnWave/dlssglom Path",
            InitialDirectory = string.IsNullOrEmpty(AnWavePathTextBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : AnWavePathTextBox.Text
        };
        if (dialog.ShowDialog() == true)
        {
            AnWavePathTextBox.Text = dialog.FolderName;
        }
    }

    private void BrowseStreamlinePath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Streamline SDK Path",
            InitialDirectory = string.IsNullOrEmpty(StreamlinePathTextBox.Text)
                ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                : StreamlinePathTextBox.Text
        };
        if (dialog.ShowDialog() == true)
        {
            StreamlinePathTextBox.Text = dialog.FolderName;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.NgxBasePath = NgxPathTextBox.Text;
        _settings.AnWavePath = AnWavePathTextBox.Text;
        _settings.StreamlinePath = StreamlinePathTextBox.Text;
        _settings.StartMinimized = StartMinimizedCheckBox.IsChecked ?? false;
        _settings.AutoScanEnabled = AutoScanCheckBox.IsChecked ?? false;
        _settings.MinimizeToTray = MinimizeToTrayCheckBox.IsChecked ?? false;
        _settings.NotifyOnNewVersion = NotifyOnNewVersionCheckBox.IsChecked ?? true;
        _settings.CheckForAppUpdates = CheckForAppUpdatesCheckBox.IsChecked ?? true;
        // v0.73: the two source preferences ship on, so an indeterminate box falls back to the
        // shipped default. The acceptance falls back to false — never granted by omission.
        _settings.IncludePreReleaseChannel = IncludePreReleaseCheckBox.IsChecked ?? true;
        _settings.AllowOtaPayloadDownloads = AllowOtaDownloadsCheckBox.IsChecked ?? true;
        _settings.OtaRedistributionAccepted = OtaRedistributionAcceptedCheckBox.IsChecked ?? false;

        await _settingsService.SaveAsync(_settings);
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}