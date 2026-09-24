using System.IO;
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
        // Loaded, not the constructor: the old `async void LoadSettings()` fired from the ctor and
        // any exception in it was unobserved — the window opened with blank fields and Save then
        // overwrote the user's real settings with those blanks.
        Loaded += async (_, _) => await LoadSettingsAsync();
    }

    private async Task LoadSettingsAsync()
    {
        SaveButton.IsEnabled = false;
        try
        {
            _settings = await _settingsService.LoadAsync();

            // v0.76: snapshot the settings as they were when this window opened, so a Save the
            // user regrets is one click from undone ("Restore previous").
            await SettingsService.WriteSnapshotAsync(_settings);
            RestorePreviousButton.IsEnabled = true;

            ApplyToControls(_settings);
            SaveButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load settings: {ex.Message}. Saving is disabled so your file is not overwritten.";
            StatusText.Visibility = Visibility.Visible;
        }

        // Show what auto-detect would pick, so "leave empty" is not a guess.
        NgxDetectedText.Text = DescribeDetected(NgxPathResolver.GetWritableBase(null));
        ValidateAll();
    }

    private void ApplyToControls(AppSettings s)
    {
        NgxPathTextBox.Text = s.NgxBasePath;
        AnWavePathTextBox.Text = s.AnWavePath;
        StreamlinePathTextBox.Text = s.StreamlinePath;
        StartMinimizedCheckBox.IsChecked = s.StartMinimized;
        AutoScanCheckBox.IsChecked = s.AutoScanEnabled;
        MinimizeToTrayCheckBox.IsChecked = s.MinimizeToTray;
        NotifyOnNewVersionCheckBox.IsChecked = s.NotifyOnNewVersion;
        CheckForAppUpdatesCheckBox.IsChecked = s.CheckForAppUpdates;
        IncludePreReleaseCheckBox.IsChecked = s.IncludePreReleaseChannel;
        AllowOtaDownloadsCheckBox.IsChecked = s.AllowOtaPayloadDownloads;
        OtaRedistributionAcceptedCheckBox.IsChecked = s.OtaRedistributionAccepted;
    }

    private static string DescribeDetected(string? path) =>
        string.IsNullOrEmpty(path) ? "Auto-detect: no NGX folder found yet" : $"Auto-detect: {path}";

    /// <summary>Warns (does not block) on a typed path that does not exist.</summary>
    private void ValidateAll()
    {
        NgxWarnText.Visibility = PathWarn(NgxPathTextBox.Text);
        AnWaveWarnText.Visibility = PathWarn(AnWavePathTextBox.Text);
        StreamlineWarnText.Visibility = PathWarn(StreamlinePathTextBox.Text);
    }

    private static Visibility PathWarn(string? path) =>
        !string.IsNullOrWhiteSpace(path) && !Directory.Exists(path.Trim())
            ? Visibility.Visible : Visibility.Collapsed;

    private void Path_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => ValidateAll();

    private void BrowseNgxPath_Click(object sender, RoutedEventArgs e) =>
        Browse(NgxPathTextBox, "Select NGX Base Path");

    private void BrowseAnWavePath_Click(object sender, RoutedEventArgs e) =>
        Browse(AnWavePathTextBox, "Select AnWave/dlssglom Path");

    private void BrowseStreamlinePath_Click(object sender, RoutedEventArgs e) =>
        Browse(StreamlinePathTextBox, "Select Streamline SDK Path");

    private static void Browse(System.Windows.Controls.TextBox target, string title)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = Directory.Exists(target.Text)
                ? target.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog() == true)
            target.Text = dialog.FolderName;
    }

    private async void RestorePrevious_Click(object sender, RoutedEventArgs e)
    {
        var previous = await SettingsService.ReadSnapshotAsync();
        if (previous == null)
        {
            StatusText.Text = "No previous settings snapshot found.";
            StatusText.Visibility = Visibility.Visible;
            return;
        }
        // Loads into the form only; nothing is written until Save.
        ApplyToControls(previous);
        StatusText.Text = "Previous settings loaded into the form. Click Save to keep them.";
        StatusText.Visibility = Visibility.Visible;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.NgxBasePath = NgxPathTextBox.Text.Trim();
        _settings.AnWavePath = AnWavePathTextBox.Text.Trim();
        _settings.StreamlinePath = StreamlinePathTextBox.Text.Trim();
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

        SaveButton.IsEnabled = false;
        try
        {
            await _settingsService.SaveAsync(_settings);
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            // async void handler: an unhandled throw here would crash the app. Report and stay open.
            SaveButton.IsEnabled = true;
            StatusText.Text = $"Could not save settings: {ex.Message}";
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
