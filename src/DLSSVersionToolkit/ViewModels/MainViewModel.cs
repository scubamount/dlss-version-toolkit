using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSVersionToolkit.Core.Models;
using DLSSVersionToolkit.Core.Services;
using System.Diagnostics;

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
private readonly IDlssIndicatorService _dlssIndicatorService;
 private readonly IWhitelistService _whitelistService;
    private readonly IPresetOverrideService _presetOverrideService;
 private ScanResult? _lastScanResult;
    private bool _shownNgxNotFoundDialog;

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
    [NotifyPropertyChangedFor(nameof(DlssIndicatorStatus))]
    private bool _isDlssIndicatorEnabled;

    public string DlssIndicatorStatus => $"DLSS Indicator: {(IsDlssIndicatorEnabled ? "On" : "Off")}";
    [ObservableProperty]
    private string _currentDlssVersion = "—";

    [ObservableProperty]
    private string _availableDlssVersion = "—";

    [ObservableProperty]
    private string _versionStatusMessage = "";

[ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private ObservableCollection<DlssPreset> _availablePresets = new();

    [ObservableProperty]
    private DlssPreset? _selectedPreset;

    [ObservableProperty]
    private string _currentPresetStatus = "";

    [ObservableProperty]
    private bool _isWhitelistApplied;

    [ObservableProperty]
    private string _whitelistStatus = "Not applied";

public MainViewModel(
 IScanService scanService,
 IUpgradeService upgradeService,
 IExportService exportService,
 ISettingsService settingsService,
 IBackupService backupService,
 IDlssDownloadService dlssDownloadService,
 IStreamlineDownloadService streamlineDownloadService,
 IAnWaveAutoService anWaveAutoService,
    IDlssIndicatorService dlssIndicatorService,
    IWhitelistService whitelistService,
    IPresetOverrideService presetOverrideService)
{
 _scanService = scanService;
 _upgradeService = upgradeService;
 _exportService = exportService;
 _settingsService = settingsService;
 _backupService = backupService;
 _dlssDownloadService = dlssDownloadService;
 _streamlineDownloadService = streamlineDownloadService;
 _anWaveAutoService = anWaveAutoService;
 _whitelistService = whitelistService;
_presetOverrideService = presetOverrideService;
_dlssIndicatorService = dlssIndicatorService;

IsDlssIndicatorEnabled = _dlssIndicatorService.IsEnabled();

 LoadPresetDefaults();
}

private void LoadPresetDefaults()
{
	// Populate the static preset list synchronously — this never touches native NVAPI
	// and must always succeed so the UI has something to bind to.
	try
	{
		AvailablePresets = new ObservableCollection<DlssPreset>(DlssPresetDisplay.AllPresets);
		SelectedPreset = AvailablePresets.FirstOrDefault();
		CurrentPresetStatus = "Detecting…";
	}
	catch (Exception ex)
	{
		Debug.WriteLine($"LoadPresetDefaults (static list) failed: {ex.Message}");
		CurrentPresetStatus = "Detection failed";
		return;
	}

	// Probe the NVIDIA driver (nvapi64.dll) OFF the UI thread. Touching
	// IPresetOverrideService.IsAvailable triggers NVIDIA.Initialize(), a P/Invoke into
	// nvapi64.dll that can block, throw, or even raise a corrupted-state exception
	// (AccessViolationException / SEHException) when the driver and wrapper disagree.
	// Doing this synchronously in the constructor (on the WPF startup thread, before the
	// main window is shown) was the cause of the silent "double-click does nothing"
	// startup crash in 0.0.20. Defer it, and isolate it in its own task so a native fault
	// is contained instead of taking down app startup.
	_ = Task.Run(DetectCurrentPresetSafe);
}

private void DetectCurrentPresetSafe()
{
	try
	{
		if (!_presetOverrideService.IsAvailable)
		{
			SetPresetStatusOnUi(null, "N/A (NvAPI unavailable)");
			return;
		}

		var current = _presetOverrideService.GetCurrentPresetAsync().GetAwaiter().GetResult();
		if (current.Success && current.CurrentPreset != null)
		{
			var preset = current.CurrentPreset.Value;
			SetPresetStatusOnUi(preset, $"Current: {DlssPresetDisplay.GetDescription(preset)}");
		}
		else
		{
			SetPresetStatusOnUi(null, current.ErrorMessage ?? "N/A");
		}
	}
	catch (Exception ex)
	{
		// Covers managed exceptions from the wrapper. Corrupted-state exceptions thrown
		// by the native driver are contained by the App-level AppDomain handler and the
		// fact that this runs on a background task, not the startup thread.
		Debug.WriteLine($"DetectCurrentPresetSafe failed: {ex.Message}");
		SetPresetStatusOnUi(null, "Detection failed");
	}
}

private void SetPresetStatusOnUi(DlssPreset? preset, string status)
{
	var dispatcher = System.Windows.Application.Current?.Dispatcher;
	void Apply()
	{
		if (preset != null)
			SelectedPreset = preset;
		CurrentPresetStatus = status;
	}

	if (dispatcher == null || dispatcher.CheckAccess())
		Apply();
	else
		dispatcher.Invoke(Apply);
}

[RelayCommand]
private async Task ApplyPresetAsync()
{
	if (SelectedPreset == null) return;

	try
	{
		// Step 0: Apply whitelist to bypass NVIDIA override blocking
		DownloadStatus = "Applying whitelist...";
		await ApplyWhitelistInternalAsync(restartServices: true, showRestartWarning: true);

		// Step 1: Apply the selected DLSS preset via NVIDIA driver settings
		DownloadStatus = $"Applying preset {DlssPresetDisplay.GetDescription(SelectedPreset.Value)}...";
		var presetResult = await _presetOverrideService.ApplyPresetAsync(SelectedPreset.Value);
		if (presetResult.Success)
		{
			CurrentPresetStatus = $"Current: {DlssPresetDisplay.GetDescription(SelectedPreset.Value)}";
			MessageBox.Show(
				$"DLSS Override Preset set to {DlssPresetDisplay.GetDescription(SelectedPreset.Value)}.\n\n" +
				"The DLSS Super Resolution override has been ENABLED (set to \"Custom\") and the " +
				"render preset applied in the NVIDIA driver's global profile — this is what makes " +
				"the preset actually take effect, equivalent to setting the override to \"Custom\" " +
				"in NVIDIA App / Profile Inspector instead of \"Use global default\".\n\n" +
				"Note: if a specific game still shows the old preset, that game has its own " +
				"per-game override set to something other than \"Use global default\". " +
				"Fully restart the game; the on-screen DLSS indicator should then show the new preset.",
				"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
		}
		else
		{
			var errMsg = presetResult.PermissionIssue
				? "Admin privileges required. Run as administrator."
				: presetResult.ErrorMessage ?? "Unknown error";
			MessageBox.Show(
				$"Failed to apply DLSS Preset {DlssPresetDisplay.GetDescription(SelectedPreset.Value)}.\n\n" +
				$"Error: {errMsg}\n\n" +
				"What to do: Ensure NVIDIA drivers are installed and try running as Administrator.",
				"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
		}

		await ScanAsync();
	}
	catch (Exception ex)
	{
		MessageBox.Show($"Apply preset failed: {ex.Message}", "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
	}
}

/// <summary>
/// Standalone "Apply Whitelist" action. Removes NVIDIA App's DLSS4 override
/// restrictions (ApplicationStorage.json + fingerprint.db) and restarts the NVIDIA
/// services so the change takes effect — the same operation as the PowerShell
/// whitelist workaround, exposed as a first-class button next to Override Preset.
/// </summary>
[RelayCommand]
private async Task ApplyWhitelistAsync()
{
	try
	{
		DownloadStatus = "Applying whitelist...";
		var applied = await ApplyWhitelistInternalAsync(restartServices: true, showRestartWarning: true);

		switch (applied)
		{
			case WhitelistOutcome.Applied:
				MessageBox.Show(
					$"Whitelist applied — {WhitelistStatus}.\n\n" +
					"NVIDIA App's DLSS4 override restrictions have been removed and the NVIDIA " +
					"services were restarted. You can now enable DLSS overrides for more games.",
					"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
				break;
			case WhitelistOutcome.AlreadyApplied:
				MessageBox.Show(
					"Whitelist is already applied — no changes were needed.",
					"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
				break;
			case WhitelistOutcome.NotApplicable:
				MessageBox.Show(
					"The NVIDIA App does not appear to be installed, so there is nothing to whitelist.\n\n" +
					"What to do: Install the NVIDIA App if you want to manage DLSS overrides through it.",
					"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
				break;
			case WhitelistOutcome.Failed:
				MessageBox.Show(
					$"Could not apply the whitelist.\n\nDetails: {WhitelistStatus}\n\n" +
					"What to do: Try running the app as Administrator and try again.",
					"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
				break;
		}
	}
	catch (Exception ex)
	{
		MessageBox.Show($"Apply whitelist failed: {ex.Message}", "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
	}
	finally
	{
		DownloadStatus = "";
	}
}

private enum WhitelistOutcome { Applied, AlreadyApplied, NotApplicable, Failed }

/// <summary>
/// Shared whitelist logic used by ApplyPreset, ApplyWhitelist, and Update All.
/// Updates WhitelistStatus / IsWhitelistApplied and optionally restarts NVIDIA services.
/// Never throws — failures are reflected in the returned outcome and WhitelistStatus.
/// </summary>
private async Task<WhitelistOutcome> ApplyWhitelistInternalAsync(bool restartServices, bool showRestartWarning)
{
	try
	{
		var result = await _whitelistService.ApplyWhitelistAsync();

		if (result.Success && result.GamesModified > 0 && result.IsApplicable)
		{
			WhitelistStatus = $"{result.GamesModified} games whitelisted";
			IsWhitelistApplied = true;

			if (restartServices)
			{
				var restart = await _whitelistService.RestartNvidiaServicesAsync();
				if (!restart.Success)
				{
					Debug.WriteLine($"ApplyWhitelistInternal: service restart failed: {restart.ErrorMessage}");
					if (showRestartWarning)
					{
						MessageBox.Show(
							$"Whitelist applied but NVIDIA services could not be restarted.\n\n" +
							$"Error: {restart.ErrorMessage}\n\n" +
							"What to do: Restart your computer or manually restart NVIDIA services.",
							"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
					}
				}
			}
			return WhitelistOutcome.Applied;
		}

		if (result.Success && result.IsApplicable)
		{
			WhitelistStatus = "Already applied";
			IsWhitelistApplied = true;
			return WhitelistOutcome.AlreadyApplied;
		}

		if (!result.IsApplicable)
		{
			WhitelistStatus = "N/A (NVIDIA app not installed)";
			IsWhitelistApplied = false;
			return WhitelistOutcome.NotApplicable;
		}

		WhitelistStatus = result.ErrorMessage ?? "Failed";
		IsWhitelistApplied = false;
		return WhitelistOutcome.Failed;
	}
	catch (Exception ex)
	{
		Debug.WriteLine($"ApplyWhitelistInternal failed: {ex.Message}");
		WhitelistStatus = "Failed";
		IsWhitelistApplied = false;
		return WhitelistOutcome.Failed;
	}
}

[RelayCommand]
    private void ToggleDlssIndicator()
    {
        var targetState = !IsDlssIndicatorEnabled;
        try
        {
            _dlssIndicatorService.SetEnabled(targetState);

            // Read back from the registry to confirm the write actually landed
            // (a silent failure here is the whole reason the indicator "did nothing").
            var raw = _dlssIndicatorService.GetRawValue();
            var actuallyEnabled = _dlssIndicatorService.IsEnabled();
            IsDlssIndicatorEnabled = actuallyEnabled;

            if (actuallyEnabled != targetState)
            {
                System.Windows.MessageBox.Show(
                    $"The DLSS Indicator registry value did not change as expected.\n\n" +
                    $"Requested: {(targetState ? "On" : "Off")}\n" +
                    $"Registry now reads: {(raw.HasValue ? raw.Value.ToString() : "(not set)")}\n\n" +
                    "Make sure the app is running as Administrator and try again.",
                    "DLSS Indicator", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (actuallyEnabled)
            {
                System.Windows.MessageBox.Show(
                    "DLSS Indicator ENABLED.\n\n" +
                    $"Registry: HKLM\\SOFTWARE\\NVIDIA Corporation\\Global\\NGXCore\\ShowDlssIndicator = {raw} (0x{raw:X}).\n\n" +
                    "The on-screen overlay (DLSS DLL version, preset, render resolution) appears in the " +
                    "top-left of supported games. You must fully restart the game for it to show — " +
                    "it will not appear on the desktop or in already-running games.",
                    "DLSS Indicator", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            else
            {
                System.Windows.MessageBox.Show(
                    "DLSS Indicator DISABLED.\n\n" +
                    "The on-screen overlay is turned off. Restart any running game for the change to take effect.",
                    "DLSS Indicator", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            // Keep the toggle state in sync with whatever the registry really says.
            try { IsDlssIndicatorEnabled = _dlssIndicatorService.IsEnabled(); } catch { /* ignore */ }
            System.Windows.MessageBox.Show(
                ex.Message,
                "DLSS Indicator", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
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
		// Pre-flight: network check
		DownloadStatus = "Checking network...";
		if (!OperationGuard.IsNetworkAvailable())
		{
			// No network — check if we have a cached SDK to fall back to
			var cachedPath = _dlssDownloadService.GetCachedDownloadPath();
			if (string.IsNullOrEmpty(cachedPath) || !File.Exists(cachedPath))
			{
				MessageBox.Show(
					"No internet connection detected and no cached DLSS SDK exists.\n\n" +
					"What to do: Connect to the internet and try again, or use 'Sync from DLSS SDK' with a previously downloaded zip.",
					"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}
		}

		// Pre-flight: disk space check (need ~500 MB for download + extract)
		var ngxBase = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
			"NVIDIA", "NGX");
		var appDataBase = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
			"DLSSVersionToolkit");

		if (!OperationGuard.HasDiskSpace(appDataBase, 500 * 1024 * 1024))
		{
			MessageBox.Show(
			"Insufficient disk space for the update operation." + Environment.NewLine +
			"At least 500 MB free space is required in the DLSSVersionToolkit data directory." + Environment.NewLine + Environment.NewLine +
			"What to do: Free up disk space and try again.",
				"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
			return;
		}

		if (Directory.Exists(ngxBase) && !OperationGuard.IsDirectoryWritable(ngxBase))
		{
			MessageBox.Show(
				"The NGX directory is not writable. Administrator access may be required.\n\n" +
				$"Path: {ngxBase}\n\n" +
				"What to do: Restart the app as Administrator and try again.",
				"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
			return;
		}

        // Step 0: Apply whitelist to bypass NVIDIA override blocking (non-fatal)
        DownloadStatus = "Applying whitelist...";
        await ApplyWhitelistInternalAsync(restartServices: true, showRestartWarning: false);

 // Step 1: Download latest DLSS SDK from NVIDIA (skips if already cached)
		DownloadStatus = "Checking for latest DLSS SDK...";
		var downloadPath = await _dlssDownloadService.DownloadLatestAsync(null);

            if (downloadPath == null)
            {
                var cachedPath = _dlssDownloadService.GetCachedDownloadPath();
                if (cachedPath == null || !File.Exists(cachedPath))
                {
                    MessageBox.Show(
                        "Could not download the latest DLSS SDK and no cached version exists.\n\n" +
                        "What happened: The download from NVIDIA/DLSS on GitHub could not be completed.\n" +
                        "What to do: Check your internet connection and try again.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }

            var sdkVersion = _dlssDownloadService.GetCachedSdkVersion() ?? "unknown";
            CachedSdkVersion = sdkVersion;
            HasCachedSdk = true;

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

            // Step 3: Apply to AnWave if installed (check all sources, including service instance)
            var anWaveTarget = !string.IsNullOrEmpty(settings.AnWavePath) ? settings.AnWavePath
                : !string.IsNullOrEmpty(AnWaveInstalledPath) ? AnWaveInstalledPath
                : !string.IsNullOrEmpty(AnWaveDetectedPath) ? AnWaveDetectedPath
                : _anWaveAutoService.GetInstalledPath();

            // Ultimate fallback: directly check known install directory on disk
            if (string.IsNullOrEmpty(anWaveTarget) || !Directory.Exists(anWaveTarget))
            {
                var knownDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DLSSVersionToolkit", "AnWave");
                if (Directory.Exists(knownDir))
                    anWaveTarget = knownDir;
            }

            if (!string.IsNullOrEmpty(anWaveTarget) && Directory.Exists(anWaveTarget))
            {
                // Use the SAME NGX base that Step 2 synced into. SyncFromCachedSdkAsync always
                // writes to %ProgramData%\NVIDIA\NGX (ngxBase below), so a custom-but-stale
                // settings.NgxBasePath would point AnWave at a folder with no freshly-synced
                // NGX_Release versions -> "Could not locate NGX Release DLL folder". Prefer the
                // configured path only when it actually exists, otherwise fall back to ngxBase.
                var anWaveNgxSource = (!string.IsNullOrEmpty(settings.NgxBasePath) && Directory.Exists(settings.NgxBasePath))
                    ? settings.NgxBasePath
                    : ngxBase;
                var anWaveOp = await _anWaveAutoService.AutoApplyAsync(anWaveTarget, anWaveNgxSource, null);
                if (!anWaveOp.Success)
                {
                    var ngxFiles = ngxOp.FilesCopied.Count > 0
                        ? string.Join("\n  • ", ngxOp.FilesCopied)
                        : "  (no files needed copying)";
                    MessageBox.Show(
                        $"Partial update — NGX succeeded but AnWave failed.\n\n" +
                        $"✅ NGX Release: v{sdkVersion} applied ({ngxOp.FilesCopied.Count} files)\n" +
                        $"  {ngxFiles}\n\n" +
                        $"❌ AnWave: {anWaveOp.ErrorMessage}\n\n" +
                        "What to do: NGX is updated. Run 'Update All' again or re-run 'Setup AnWave' to fix AnWave.",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    var ngxStatus = ngxOp.FilesCopied.Count > 0
                        ? $"v{sdkVersion} updated ({ngxOp.FilesCopied.Count} files)"
                        : "already up to date";
                    var ngxDetail = ngxOp.FilesCopied.Count > 0
                        ? string.Join("\n  • ", ngxOp.FilesCopied)
                        : "  (no files needed)";
                    var anWaveFiles = string.Join("\n  • ", anWaveOp.FilesCopied);
                    var appliedVer = anWaveOp.AppliedVersion ?? sdkVersion;
                    MessageBox.Show(
                        $"All done!\n\n" +
                        $"✅ NGX Release: {ngxStatus}\n" +
                        $"  {ngxDetail}\n\n" +
                        $"✅ AnWave: v{appliedVer} applied ({anWaveOp.FilesCopied.Count} files)\n" +
                        $"  {anWaveFiles}\n\n" +
                        "DLSS Override is now globally active.\n" +
                        "Games using DLSS will use v" + appliedVer + ".",
                        "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            }
		else
		{
			// AnWave not detected — auto-setup then apply
			DownloadStatus = "Setting up AnWave...";

			var setupProgress = new Progress<int>(pct =>
			{
				if (pct < 30) DownloadStatus = $"Downloading nvidiaDlssGlom... {pct}%";
				else if (pct < 70) DownloadStatus = $"Downloading DLSS DLLs... {pct}%";
				else DownloadStatus = $"Activating override... {pct}%";
			});

			var setupResult = await _anWaveAutoService.SetupAnWaveAsync(setupProgress);

			if (setupResult.Success)
			{
				IsAnWaveInstalled = true;
				AnWaveInstalledPath = setupResult.InstalledPath ?? "";
				AnWaveGlomVersion = setupResult.GlomVersion ?? "";
				AnWaveDllVersion = setupResult.DllVersion ?? "";
				AnWaveDetectedPath = setupResult.InstalledPath ?? "";
		IsAnWaveDetected = true;

		// Persist AnWave path to settings so subsequent scans find it
		try
		{
			var currentSettings = await _settingsService.LoadAsync();
			if (string.IsNullOrEmpty(currentSettings.AnWavePath) && !string.IsNullOrEmpty(setupResult.InstalledPath))
			{
				currentSettings.AnWavePath = setupResult.InstalledPath;
				await _settingsService.SaveAsync(currentSettings);
			}
		}
		catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save AnWave path to settings: {ex.Message}"); }
				// Re-resolve AnWave path after setup
				anWaveTarget = setupResult.InstalledPath;

				// Apply NGX DLLs to AnWave folder — use the same NGX base Step 2 synced into.
				var anWaveNgxSource = (!string.IsNullOrEmpty(settings.NgxBasePath) && Directory.Exists(settings.NgxBasePath))
					? settings.NgxBasePath
					: ngxBase;
				var anWaveOp = await _anWaveAutoService.AutoApplyAsync(anWaveTarget!, anWaveNgxSource, null);

				var ngxFiles = ngxOp.FilesCopied.Count > 0
					? string.Join("\n • ", ngxOp.FilesCopied)
					: " (no files needed)";
				var ngxStatus = ngxOp.FilesCopied.Count > 0
					? $"v{sdkVersion} updated ({ngxOp.FilesCopied.Count} files)"
					: "already up to date";

				if (!anWaveOp.Success)
				{
					MessageBox.Show(
						$"Partial update — NGX succeeded but AnWave apply failed after setup.\n\n" +
						$"✅ NGX Release: {ngxStatus}\n" +
						$" {ngxFiles}\n\n" +
						$"❌ AnWave apply: {anWaveOp.ErrorMessage}\n\n" +
						"What to do: NGX is updated. Run 'Update All' again or re-run 'Setup AnWave' to fix AnWave.",
						"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
				}
				else
				{
					var anWaveFiles = string.Join("\n • ", anWaveOp.FilesCopied);
					var appliedVer = anWaveOp.AppliedVersion ?? sdkVersion;
					MessageBox.Show(
						$"All done!\n\n" +
						$"✅ NGX Release: {ngxStatus}\n" +
						$" {ngxFiles}\n\n" +
						$"✅ AnWave setup + apply: v{appliedVer} ({anWaveOp.FilesCopied.Count} files)\n" +
						$" {anWaveFiles}\n\n" +
						"DLSS Override is now globally active.\n" +
						"Games using DLSS will use v" + appliedVer + ".",
						"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
				}
			}
			else
			{
				var ngxFiles = ngxOp.FilesCopied.Count > 0
					? string.Join("\n • ", ngxOp.FilesCopied)
					: " (already up to date)";
				var versionStatus = ngxOp.FilesCopied.Count > 0
					? $"NGX Release updated to v{sdkVersion}."
					: $"NGX Release already at v{sdkVersion}.";
				MessageBox.Show(
					$"{versionStatus}\n\n" +
					$"Files copied ({ngxOp.FilesCopied.Count}):\n" +
					$" {ngxFiles}\n\n" +
					$"❌ AnWave auto-setup failed: {setupResult.ErrorMessage}\n\n" +
					"What to do: NGX is updated. Try 'Setup AnWave' separately from the Advanced menu.",
					"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
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

            // Show dialog once when NGX versions were not found at any checked path
            var hasNgxSources = result.Sources.Any(s => s.Source?.StartsWith("NGX_") == true);
            if (!hasNgxSources && result.NgxPathsChecked.Count > 0 && !_shownNgxNotFoundDialog)
            {
                _shownNgxNotFoundDialog = true;
                var paths = string.Join("\n  • ", result.NgxPathsChecked);
                System.Windows.MessageBox.Show(
                    $"NGX versions were not found at any known location.\n\n" +
                    $"Paths checked:\n  • {paths}\n\n" +
                    "This usually means:\n" +
                    "  • DLSS Override is not yet enabled in the NVIDIA App\n" +
                    "  • Or the NGX directory was moved or deleted\n\n" +
                    "What to do:\n" +
                    "  1. Open the NVIDIA App and enable DLSS Override for at least one game\n" +
                    "  2. Then click Scan again\n" +
                    "  Or set a custom NGX path in Settings if yours is in a non-default location",
                    "DLSS Version Toolkit", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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
                        "What to do next: Your NGX Release is now updated. If you use AnWave, run 'Update All' to keep it in sync.",
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
                        "What to do next: If you use AnWave, run 'Update All' to keep it in sync.",
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
                        "What to do next: If you use AnWave, run 'Update All' to keep it in sync.",
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
                        "What to do next: If you use AnWave, run 'Update All' to keep it in sync.",
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
                        "What to do: NGX is updated. Run 'Update All' to re-apply or re-run 'Setup AnWave'.",
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
        catch (Exception ex)
        {
            Debug.WriteLine($"FindAnWaveInDownloads: error: {ex.Message}");
        }

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
}
