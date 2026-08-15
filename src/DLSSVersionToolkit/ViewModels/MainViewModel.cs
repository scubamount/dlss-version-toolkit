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
    private bool _isApplyingPreset;

    // Determinate apply progress (v0.0.39). Value 0-100; indeterminate only until the first
    // progress report arrives (profile total unknown before enumeration).
    [ObservableProperty]
    private int _applyProgressValue;

    [ObservableProperty]
    private bool _applyProgressIndeterminate = true;

    [ObservableProperty]
    private bool _isIndexingProfiles;

    [ObservableProperty]
    private string _cachedStreamlineVersion = "";

    [ObservableProperty]
    private bool _hasCachedStreamline;

    // Streamline SDK version shown in the hero strip (v0.0.38). NGX version folders contain
    // only nvngx_*.dll — never sl.common.dll — so a per-NGX-row Streamline version cannot
    // exist; this surfaces the best available signal instead: the scanned Streamline SDK
    // entry (sl.common.dll FileVersionInfo) or, failing that, the cached SDK download version.
    [ObservableProperty]
    private string _streamlineVersion = "—";

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

    // DLSS-RR (Ray Reconstruction) and DLSS-FG (Frame Generation) each have their OWN preset
    // selection, independent of the SR preset above. Defaults: RR=E (best quality), FG=B.
    [ObservableProperty]
    private ObservableCollection<DlssPreset> _availableRrPresets = new();

    [ObservableProperty]
    private DlssPreset _selectedRrPreset = DlssPresetDisplay.RayReconstructionDefault;

    [ObservableProperty]
    private ObservableCollection<DlssPreset> _availableFgPresets = new();

    [ObservableProperty]
    private DlssPreset _selectedFgPreset = DlssPresetDisplay.FrameGenerationDefault;

    // DLSSG generator mode + multiplier (the Fixed/Dynamic + 2x/3x/4x… knobs). SEPARATE from the
    // FG preset above — these were previously unsettable from the toolkit, so users had to use the
    // NVIDIA App ("Dynamic, up to 6x, at max refresh rate") to get FG behaving. Default mode =
    // Dynamic, multiplier = 4x, target = AUTO (match max refresh rate).
    [ObservableProperty]
    private ObservableCollection<DlssgMode> _availableFgModes = new();

    [ObservableProperty]
    private DlssgMode _selectedFgMode = DlssPresetDisplay.FrameGenModeDefault;

    [ObservableProperty]
    private ObservableCollection<int> _availableFgMultipliers = new();

    [ObservableProperty]
    private int _selectedFgMultiplier = DlssPresetDisplay.FrameGenMultiplierDefault;  // 6x
    private bool _fgMultiplierEnabled = true;
    // FG multiplier only has meaning when the mode is Fixed or Dynamic — in Off/Auto/
    // Don't change the driver ignores it. (v0.0.47) gate the secondary knob off its parent.
    public bool FgMultiplierEnabled { get => _fgMultiplierEnabled; set { if (_fgMultiplierEnabled != value) { _fgMultiplierEnabled = value; OnPropertyChanged(); } } }
    partial void OnSelectedFgModeChanged(DlssgMode value)
    {
        FgMultiplierEnabled = value == DlssgMode.Fixed || value == DlssgMode.Dynamic;
    }

    [ObservableProperty]
    private string _currentPresetStatus = "";

    [ObservableProperty]
    private bool _isWhitelistApplied;

    [ObservableProperty]
    private string _whitelistStatus = "Not applied";

    // --- App self-update (v0.0.31) ---

    [ObservableProperty]
    private bool _appUpdateAvailable;

    [ObservableProperty]
    private string _appUpdateVersion = "";

    [ObservableProperty]
    private bool _isApplyingAppUpdate;

    private AppUpdateInfo? _pendingAppUpdate;

    // --- First-run quick guide (v0.0.31) ---

    [ObservableProperty]
    private bool _isQuickGuideVisible;

    private readonly AppUpdateService _appUpdateService = new();
    private readonly IVersionComparer _versionComparer;
    private readonly UpdateRunReportManager _runReports = new();

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
    IPresetOverrideService presetOverrideService,
    IVersionComparer versionComparer)
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
_versionComparer = versionComparer;

IsDlssIndicatorEnabled = _dlssIndicatorService.IsEnabled();

 LoadPresetDefaults();

    // Kick the app self-update check + quick-guide visibility off the UI thread.
    // Both are best-effort: failures stay silent (Debug log only).
    _ = Task.Run(InitializeStartupStateAsync);
}

    /// <summary>
    /// Background startup work: decide whether to show the first-run quick guide and
    /// check GitHub for a newer app version. All properties set here are scalars
    /// (bool/string) — WPF's binding engine marshals INPC notifications for scalar
    /// properties to the UI thread automatically (collections would NOT be safe).
    /// </summary>
    private async Task InitializeStartupStateAsync()
    {
        try
        {
            var settings = await _settingsService.LoadAsync();

            // Restore the persisted preset selections (v0.0.38). These are scalar properties,
            // so setting them from this background task is safe (WPF marshals scalar INPC).
            // Precedence: driver truth (DetectCurrentPresetSafeAsync, when it succeeds) >
            // saved selection (here) > recommended default (LoadPresetDefaults). Detection
            // runs concurrently and overwrites SelectedPreset only on SUCCESS, so a failed
            // or unavailable NvAPI probe no longer resets the user's choice to L on launch.
            RestorePresetSelections(settings);

            if (!settings.HasSeenQuickGuide)
                IsQuickGuideVisible = true;

            if (settings.CheckForAppUpdates)
            {
                var info = await _appUpdateService.CheckForUpdateAsync();
                if (info.IsUpdateAvailable)
                {
                    _pendingAppUpdate = info;
                    AppUpdateVersion = info.LatestVersion;
                    AppUpdateAvailable = true;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"InitializeStartupStateAsync (non-fatal): {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task DismissQuickGuideAsync()
    {
        IsQuickGuideVisible = false;
        try
        {
            var settings = await _settingsService.LoadAsync();
            if (!settings.HasSeenQuickGuide)
            {
                settings.HasSeenQuickGuide = true;
                await _settingsService.SaveAsync(settings);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DismissQuickGuide save failed (non-fatal): {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowQuickGuide()
    {
        // Re-show on demand from the sidebar; does NOT reset the persisted flag.
        IsQuickGuideVisible = true;
    }

    [RelayCommand]
    private async Task ApplyAppUpdateAsync()
    {
        if (_pendingAppUpdate is not { } update || IsApplyingAppUpdate) return;

        var sizeMb = update.AssetSize > 0 ? $" (~{update.AssetSize / 1024.0 / 1024.0:F1} MB)" : "";
        var confirm = MessageBox.Show(
            $"Update DLSS Version Toolkit from v{update.CurrentVersion} to v{update.LatestVersion}?\n\n" +
            $"The new version{sizeMb} will be downloaded and the app will restart.\n\n" +
            "Your settings and cached downloads are kept.",
            "App Update", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        IsApplyingAppUpdate = true;
        DownloadStatus = $"Downloading v{update.LatestVersion}...";
        try
        {
            var progress = new Progress<int>(pct =>
                DownloadStatus = $"Downloading v{update.LatestVersion}... {pct}%");
            var result = await _appUpdateService.DownloadAndApplyAsync(update, progress);

            if (!result.Success)
            {
                MessageBox.Show(result.ErrorMessage, "App Update",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DownloadStatus = "";
            var restart = MessageBox.Show(
                $"v{update.LatestVersion} is installed.\n\nRestart now to finish the update?",
                "App Update", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (restart == MessageBoxResult.Yes)
            {
                AppUpdateService.RestartForUpdate(result.ExePath,
                    () => Application.Current.Shutdown());
            }
            else
            {
                // Already swapped on disk; next manual launch runs the new version.
                AppUpdateAvailable = false;
                StatusMessage = $"v{update.LatestVersion} will run after the next restart.";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Update failed: {ex.Message}\n\n" +
                $"What to do: download the new version manually from {AppUpdateService.ReleasesPageUrl}",
                "App Update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsApplyingAppUpdate = false;
            DownloadStatus = "";
        }
    }

private void LoadPresetDefaults()
{
	// Populate the static preset list synchronously — this never touches native NVAPI
	// and must always succeed so the UI has something to bind to.
	try
	{
		AvailablePresets = new ObservableCollection<DlssPreset>(DlssPresetDisplay.SuperResolutionPresets);
		SelectedPreset = DlssPresetDisplay.SuperResolutionDefault;  // L

		AvailableRrPresets = new ObservableCollection<DlssPreset>(DlssPresetDisplay.RayReconstructionPresets);
		SelectedRrPreset = DlssPresetDisplay.RayReconstructionDefault;  // E

		AvailableFgPresets = new ObservableCollection<DlssPreset>(DlssPresetDisplay.FrameGenerationPresets);
		SelectedFgPreset = DlssPresetDisplay.FrameGenerationDefault;  // B

		AvailableFgModes = new ObservableCollection<DlssgMode>(DlssPresetDisplay.FrameGenModes);
		SelectedFgMode = DlssPresetDisplay.FrameGenModeDefault;  // Dynamic
		AvailableFgMultipliers = new ObservableCollection<int>(DlssPresetDisplay.FrameGenMultipliers);
		SelectedFgMultiplier = DlssPresetDisplay.FrameGenMultiplierDefault;  // 4x

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
	_ = Task.Run(DetectCurrentPresetSafeAsync);
	}

	private async Task DetectCurrentPresetSafeAsync()
	{
		try
		{
			if (!_presetOverrideService.IsAvailable)
			{
				SetPresetStatusOnUi(null, "N/A (NvAPI unavailable)");
				return;
			}

			var current = await _presetOverrideService.GetCurrentPresetAsync();
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

/// <summary>
/// Builds the per-feature override options from the current RR/FG dropdown selections.
/// The SR preset is passed separately as the ApplyPresetAsync preset argument.
/// </summary>
private PresetApplyOptions BuildPresetOptions() => new()
{
	RayReconstructionPreset = SelectedRrPreset,
	FrameGenerationPreset = SelectedFgPreset,
	FrameGenerationMode = SelectedFgMode,
	FrameGenerationMultiplier = SelectedFgMultiplier,
	// Dynamic target FPS left null = AUTO ("match max refresh rate"). A future settings field
	// can surface an explicit FPS target; null is the NVIDIA App default.
	FrameGenerationDynamicTargetFps = null,
};

/// <summary>
/// Restores saved preset selections from settings (v0.0.38). Unknown/empty stored values
/// leave the recommended defaults from LoadPresetDefaults() untouched.
/// </summary>
private void RestorePresetSelections(AppSettings settings)
{
	try
	{
		if (PresetSelectionPersistence.ParsePreset(settings.SelectedSrPreset) is { } sr)
			SelectedPreset = sr;
		if (PresetSelectionPersistence.ParsePreset(settings.SelectedRrPreset) is { } rr)
			SelectedRrPreset = rr;
		if (PresetSelectionPersistence.ParsePreset(settings.SelectedFgPreset) is { } fg)
			SelectedFgPreset = fg;
		if (PresetSelectionPersistence.ParseMode(settings.SelectedFgMode) is { } mode)
			SelectedFgMode = mode;
		if (PresetSelectionPersistence.ParseMultiplier(settings.SelectedFgMultiplier) is { } mult)
			SelectedFgMultiplier = mult;
	}
	catch (Exception ex)
	{
		Debug.WriteLine($"RestorePresetSelections failed (non-fatal, keeping defaults): {ex.Message}");
	}
}

/// <summary>
/// Persists the current preset selections (v0.0.38). Called after a successful Apply — the
/// durable point of user intent — rather than on every dropdown click, so transient browsing
/// through the list never overwrites the last applied choice. Non-fatal on failure.
/// </summary>
private async Task SavePresetSelectionsAsync()
{
	try
	{
		var settings = await _settingsService.LoadAsync();
		PresetSelectionPersistence.ApplyTo(settings,
			SelectedPreset, SelectedRrPreset, SelectedFgPreset,
			SelectedFgMode, SelectedFgMultiplier);
		await _settingsService.SaveAsync(settings);
	}
	catch (Exception ex)
	{
		Debug.WriteLine($"SavePresetSelections failed (non-fatal): {ex.Message}");
	}
}

/// <summary>Pads a dotted version to 4 parts for display ("310.7.0" → "310.7.0.0").</summary>
private static string PadVersionTo4(string v)
{
	var parts = v.Split('.');
	return parts.Length >= 4 ? v : v + string.Concat(Enumerable.Repeat(".0", 4 - parts.Length));
}

/// <summary>
/// Stops all Update All progress indicators (v0.0.39). MUST be called immediately before any
/// terminal MessageBox inside OneClickUpdateAllAsync: the dialogs are modal, so the finally
/// block that clears these flags doesn't run until the user dismisses the popup — the bar
/// kept animating behind "All done!", hiding when the work actually finished.
/// </summary>
private void EndUpdateAllProgress()
{
	IsUpdatingAll = false;
	ScanStatus = "Ready";
	DownloadStatus = "";
}

[RelayCommand]
private async Task ApplyPresetAsync()
{
	if (SelectedPreset == null) return;

	IsApplyingPreset = true;
	ApplyProgressValue = 0;
	ApplyProgressIndeterminate = true;
	PresetOverrideResult? presetResult = null;
	try
	{
		// Step 0: Apply whitelist to bypass NVIDIA override blocking
		DownloadStatus = "Applying whitelist...";
		await ApplyWhitelistInternalAsync(restartServices: true, showRestartWarning: true);

		// Step 1: Apply the selected DLSS preset via NVIDIA driver settings.
		DownloadStatus = $"Applying preset {DlssPresetDisplay.GetDescription(SelectedPreset.Value)} to all game profiles...";
		presetResult = await _presetOverrideService.ApplyPresetAsync(
			SelectedPreset.Value, BuildPresetOptions(), MakeApplyProgress());
		if (presetResult.Success)
		{
			CurrentPresetStatus = $"Current: {DlssPresetDisplay.GetDescription(SelectedPreset.Value)}";

			// Persist the applied selections so they survive an app restart (v0.0.38 —
			// fixes "preset resets to L on relaunch").
			await SavePresetSelectionsAsync();
		}
	}
	catch (Exception ex)
	{
		MessageBox.Show($"Apply preset failed: {ex.Message}", "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
	}
	finally
	{
		// Stop the progress indicator BEFORE any dialog (v0.0.39). Previously the success
		// MessageBox fired inside the try block with IsApplyingPreset still true, so the
		// indeterminate bar kept animating behind the "done" popup — the user couldn't
		// tell when the work actually finished.
		IsApplyingPreset = false;
		DownloadStatus = "";
	}

	if (presetResult == null) return;

	if (presetResult.Success)
	{
		MessageBox.Show(
			$"DLSS Override Preset set to {DlssPresetDisplay.GetDescription(SelectedPreset.Value)}.\n\n" +
			$"Applied to {presetResult.ProfilesUpdated} driver profile(s), including " +
			$"{presetResult.GameProfilesUpdated} game profile(s), in {presetResult.ElapsedMs / 1000.0:F1}s" +
			(presetResult.UsedIndex ? " (fast path — profile index)" : " (full scan — index rebuilt for next time)") + ".\n\n" +
			"For every affected game the DLSS Super Resolution override was ENABLED " +
			"(\"Custom\") and the render preset set, plus the Ray Reconstruction and " +
			"Frame Generation DLL overrides enabled.\n\n" +
			"Fully restart the game; the on-screen DLSS indicator (bottom-left) should then " +
			"show the new preset and DLL version.",
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

/// <summary>
/// Progress adapter for ApplyPresetAsync (v0.0.39): first report switches the bar from
/// indeterminate to determinate; subsequent reports map (done,total) onto 0-100.
/// </summary>
private Progress<(int Done, int Total)> MakeApplyProgress() => new(p =>
{
	if (p.Total <= 0) return;
	ApplyProgressIndeterminate = false;
	ApplyProgressValue = (int)(p.Done * 100L / p.Total);
	DownloadStatus = $"Applying to game profiles... {p.Done}/{p.Total}";
});

/// <summary>
/// Manual "Index Game Profiles" action (v0.0.39). Scans the ~8000 driver profiles once and
/// persists the game set, so Apply/Update All take the fast path. Auto-invalidated on
/// driver change; also rebuilt automatically whenever an apply runs the full scan — this
/// button exists for users who want to pay the scan cost up front.
/// </summary>
[RelayCommand]
private async Task IndexProfilesAsync()
{
	if (IsIndexingProfiles || IsApplyingPreset || IsUpdatingAll) return;

	var confirm = MessageBox.Show(
		"Index game profiles now?\n\n" +
		"This scans all NVIDIA driver profiles once (a few seconds) and remembers which ones " +
		"belong to installed games. Future 'Apply to all games' and 'Update All' runs will use " +
		"this index and be significantly faster.\n\n" +
		"The index refreshes automatically when your NVIDIA driver changes.",
		"DLSS Version Toolkit", MessageBoxButton.YesNo, MessageBoxImage.Question);
	if (confirm != MessageBoxResult.Yes) return;

	IsIndexingProfiles = true;
	DownloadStatus = "Indexing game profiles...";
	try
	{
		var result = await _presetOverrideService.RebuildProfileIndexAsync();
		DownloadStatus = "";
		if (result.Success)
		{
			MessageBox.Show(
				$"Indexed {result.GameProfilesUpdated} game profile(s) in {result.ElapsedMs / 1000.0:F1}s.\n\n" +
				"Apply to all games and Update All will now use the fast path.",
				"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
		}
		else
		{
			MessageBox.Show(
				$"Indexing failed: {result.ErrorMessage}\n\n" +
				"Applies will keep working — they just use the full scan.",
				"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
		}
	}
	catch (Exception ex)
	{
		MessageBox.Show($"Indexing failed: {ex.Message}", "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
	}
	finally
	{
		IsIndexingProfiles = false;
		DownloadStatus = "";
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
/// Unlocks NVIDIA App's DLSS Override UI for games it reports as "not supported" by setting
/// IsOpsSupported:true on NVIDIA-identified entries. Separate from Apply Whitelist because the
/// field is undocumented — the user opts in explicitly and is told a .bak was written.
/// </summary>
[RelayCommand]
private async Task UnlockUnsupportedGamesAsync()
{
	try
	{
		DownloadStatus = "Unlocking unsupported games...";
		var result = await _whitelistService.UnlockUnsupportedGamesAsync();

		if (!result.IsApplicable)
		{
			MessageBox.Show(
				"The NVIDIA App does not appear to be installed, so there is nothing to unlock.",
				"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
			return;
		}

		if (!result.Success)
		{
			MessageBox.Show(
				$"Could not unlock unsupported games.\n\nDetails: {result.ErrorMessage}\n\n" +
				"What to do: close the NVIDIA App completely (including the system tray icon), " +
				"then run this app as Administrator and try again.",
				"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
			return;
		}

		if (result.GamesModified == 0)
		{
			MessageBox.Show(
				"No games needed unlocking — every game the NVIDIA App has detected already " +
				"reports DLSS override support.",
				"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
			return;
		}

		var restart = await _whitelistService.RestartNvidiaServicesAsync();
		var restartNote = restart.Success
			? "The NVIDIA services were restarted."
			: $"NVIDIA services could not be restarted ({restart.ErrorMessage}) — reboot for the change to take effect.";

		MessageBox.Show(
			$"Unlocked {result.GamesModified} game(s) that the NVIDIA App reported as not supported.\n\n" +
			$"{restartNote}\n\n" +
			"Open the NVIDIA App → Graphics → Program Settings to set DLSS overrides for them.\n\n" +
			"Note: a backup was saved as ApplicationStorage.json.bak. The NVIDIA App may undo this " +
			"when it re-scans your library or updates — just run this again if a game reverts.",
			"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Information);
	}
	catch (Exception ex)
	{
		MessageBox.Show($"Unlock unsupported games failed: {ex.Message}", "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
	}
	finally
	{
		DownloadStatus = "";
	}
}

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
                    "bottom-left of supported games. You must fully restart the game for it to show — " +
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

        // Run report (v0.0.47): collect every step's outcome for the drawer and a persisted
        // file, instead of a success dialog that vanishes on OK.
        _runReports.Begin($"{AppUpdateService.GetCurrentVersion()}");
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
        var wlOutcome = await ApplyWhitelistInternalAsync(restartServices: true, showRestartWarning: false);
        _runReports.Add("Whitelist", wlOutcome == WhitelistOutcome.Applied ? "ok" : "warn",
            wlOutcome == WhitelistOutcome.Applied ? "NVIDIA App overrides removed"
            : wlOutcome == WhitelistOutcome.AlreadyApplied ? "already applied"
            : wlOutcome == WhitelistOutcome.Failed ? "could not modify the override file"
            : "NVIDIA App not found");

        // Step 0a: Unlock games the NVIDIA App reports as "not supported" (IsOpsSupported).
        // Non-fatal, but NOT silent — v0.0.42 taught us a swallowed Debug-only failure can hide
        // a broken step for releases. The outcome goes in the summary either way.
        string unlockLine;
        try
        {
            DownloadStatus = "Unlocking unsupported games...";
            var unlockResult = await _whitelistService.UnlockUnsupportedGamesAsync();
            _runReports.Add("Unlock", unlockResult.Success && unlockResult.GamesModified > 0 ? "warn" : "ok",
                unlockResult.Success
                    ? $"unlocked {unlockResult.GamesModified} game(s)"
                    : "could not modify the App library file");
            unlockLine =
                !unlockResult.IsApplicable ? ""
                : !unlockResult.Success
                    ? $"⚠️ Unlock unsupported games FAILED: {unlockResult.ErrorMessage}\n"
                : unlockResult.GamesModified > 0
                    ? $"✅ Unlocked {unlockResult.GamesModified} game(s) the NVIDIA App reported as not supported\n"
                    : "✅ Unsupported games: none needed unlocking\n";
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"OneClickUpdateAll: unlock threw (non-fatal): {ex.Message}");
            unlockLine = $"⚠️ Unlock unsupported games FAILED: {ex.Message}\n";
        }

        // Step 0b: Push the selected DLSS preset to every game profile (enables the
        // SR/RR/FG overrides as "Custom"). Without this the games keep their own
        // per-profile defaults and ignore the global preset. Non-fatal.
        if (SelectedPreset is { } presetToApply && presetToApply != DlssPreset.Default
            && _presetOverrideService.IsAvailable)
        {
            try
            {
                DownloadStatus = $"Applying preset {DlssPresetDisplay.GetDescription(presetToApply)} to all games...";
                var pr = await _presetOverrideService.ApplyPresetAsync(presetToApply, BuildPresetOptions(), MakeApplyProgress());
                if (pr.Success)
                {
                    Debug.WriteLine($"OneClickUpdateAll: preset applied to {pr.GameProfilesUpdated} game profile(s) in {pr.ElapsedMs}ms (index={pr.UsedIndex})");
                    // Persist the applied selections (v0.0.38) — Update All is the main apply
                    // path for most users, so it must save too, not just the Apply button.
                    await SavePresetSelectionsAsync();
                    _runReports.Add("Presets", "ok", $"{DlssPresetDisplay.GetShortLabel(presetToApply)} applied to {pr.GameProfilesUpdated} game profile(s)");
                }
                else
                {
                    Debug.WriteLine($"OneClickUpdateAll: preset apply non-fatal failure: {pr.ErrorMessage}");
                    _runReports.Add("Presets", "warn", pr.ErrorMessage ?? "apply failed");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OneClickUpdateAll: preset apply threw (non-fatal): {ex.Message}");
                _runReports.Add("Presets", "warn", ex.Message);
            }
        }

 // Step 1a: Download + sync the Streamline SDK FIRST. It is the COMPREHENSIVE source — it
		// bundles all four NGX DLLs (nvngx_dlss/dlssg/dlssd/deepdvc), whereas the NVIDIA/DLSS
		// demo zip ships ONLY nvngx_dlss.dll. Syncing Streamline first populates FrameGen / Ray
		// Reconstruction / DeepDVC; the DLSS demo sync (Step 1b) then lays the latest SR DLL on
		// top. This is why a DLSS-only Update All never updated Streamline-provided components.
		// Non-fatal: if Streamline can't be fetched we still proceed with the DLSS SDK.
		string? streamlineVersion = null;
		UpgradeOperation? streamlineOp = null;
		try
		{
			DownloadStatus = "Checking for latest Streamline SDK...";
			var slPath = await _streamlineDownloadService.DownloadLatestAsync(null);
			if (slPath != null)
			{
				streamlineVersion = _streamlineDownloadService.GetCachedSdkVersion();
				CachedStreamlineVersion = streamlineVersion ?? "";
				HasCachedStreamline = true;
				DownloadStatus = $"Applying Streamline SDK v{streamlineVersion} to NGX...";
				streamlineOp = await _streamlineDownloadService.SyncFromCachedSdkAsync(null);
				if (streamlineOp != null && streamlineOp.Status == OperationStatus.Failed)
				{
					Debug.WriteLine($"OneClickUpdateAll: Streamline NGX sync non-fatal failure: {streamlineOp.ErrorMessage}");
					_runReports.Add("Streamline", "fail", streamlineOp.ErrorMessage ?? "sync failed");
				}
				else
				{
					_runReports.Add("Streamline", "ok", $"latest v{streamlineVersion} synced");
				}
			}
			else
			{
				Debug.WriteLine("OneClickUpdateAll: Streamline download returned null (offline or no asset); skipping.");
				_runReports.Add("Streamline", "info", "no update available (offline or no newer asset)");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"OneClickUpdateAll: Streamline step threw (non-fatal): {ex.Message}");
			_runReports.Add("Streamline", "warn", ex.Message);
		}

 // Step 1b: Download latest DLSS SDK from NVIDIA (skips if already cached)
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
                _runReports.Add("NGX sync", "fail",
                    $"rolled back ({ngxOp.ErrorMessage}); backup at {ngxOp.BackupPath}");
                MessageBox.Show(
                    $"DLSS SDK v{sdkVersion} sync to NGX failed and was rolled back.\n\n" +
                    $"Error: {ngxOp.ErrorMessage}\n" +
                    $"Backup preserved at: {ngxOp.BackupPath}\n\n" +
                    "What to do: Your previous NGX files have been restored. Check the error message above and try again.",
                    "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            _runReports.Add("DLSS SDK", "ok",
                $"v{sdkVersion} synced to NGX ({ngxOp.FilesCopied.Count} file(s))");

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
                    _runReports.Add("AnWave", "fail", anWaveOp.ErrorMessage ?? "apply failed");
                    var ngxFiles = ngxOp.FilesCopied.Count > 0
                        ? string.Join("\n  • ", ngxOp.FilesCopied)
                        : "  (no files needed copying)";
                    EndUpdateAllProgress();
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
                    _runReports.Add("AnWave", "ok",
                        $"v{anWaveOp.AppliedVersion ?? sdkVersion} applied ({anWaveOp.FilesCopied.Count} file(s))");
                    var ngxStatus = ngxOp.FilesCopied.Count > 0
                        ? $"v{sdkVersion} updated ({ngxOp.FilesCopied.Count} files)"
                        : "already up to date";
                    var ngxDetail = ngxOp.FilesCopied.Count > 0
                        ? string.Join("\n  • ", ngxOp.FilesCopied)
                        : "  (no files needed)";
                    var anWaveFiles = string.Join("\n  • ", anWaveOp.FilesCopied);
                    var appliedVer = anWaveOp.AppliedVersion ?? sdkVersion;
                    // v0.0.42: honest Streamline line. The old one showed "✅ synced (0 files)"
                    // even when the sync FAILED — which hid the bin\x64 doubling bug for 5 releases.
                    var slLine =
                        string.IsNullOrEmpty(streamlineVersion) ? "ℹ️ Streamline SDK: not updated (offline or unavailable)\n"
                        : streamlineOp == null || streamlineOp.Status == OperationStatus.Failed
                            ? $"⚠️ Streamline SDK: v{streamlineVersion} downloaded but NGX sync FAILED: {streamlineOp?.ErrorMessage ?? "no cached zip"}\n"
                        : streamlineOp.FilesCopied.Count > 0
                            ? $"✅ Streamline SDK: v{streamlineVersion} synced ({streamlineOp.FilesCopied.Count} files)\n"
                            : $"✅ Streamline SDK: v{streamlineVersion} already applied (no files needed)\n";
                    EndUpdateAllProgress();
                    MessageBox.Show(
                        $"All done!\n\n" +
                        unlockLine +
                        slLine +
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
					_runReports.Add("AnWave", "fail", anWaveOp.ErrorMessage ?? "apply failed");
					EndUpdateAllProgress();
					MessageBox.Show(
						$"Partial update — NGX succeeded but AnWave apply failed after setup.\n\n" +
						unlockLine +
						$"✅ NGX Release: {ngxStatus}\n" +
						$" {ngxFiles}\n\n" +
						$"❌ AnWave apply: {anWaveOp.ErrorMessage}\n\n" +
						"What to do: NGX is updated. Run 'Update All' again or re-run 'Setup AnWave' to fix AnWave.",
						"DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Warning);
				}
				else
				{
					_runReports.Add("AnWave", "ok",
						$"v{anWaveOp.AppliedVersion ?? sdkVersion} applied ({anWaveOp.FilesCopied.Count} file(s))");
					var anWaveFiles = string.Join("\n • ", anWaveOp.FilesCopied);
					var appliedVer = anWaveOp.AppliedVersion ?? sdkVersion;
					EndUpdateAllProgress();
					MessageBox.Show(
						$"All done!\n\n" +
						unlockLine +
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
				EndUpdateAllProgress();
				MessageBox.Show(
					$"{versionStatus}\n\n" +
					unlockLine +
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
            _runReports.Add("Update All", "fail", ex.Message);
            MessageBox.Show(
                $"Update failed: {ex.Message}\n\n" +
                "What to do: Check the error above. If it's a network issue, try again. If it's a file access issue, ensure no other programs are using the NGX or AnWave directories.",
                "DLSS Version Toolkit", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _runReports.Finish();
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

            // Streamline hero value (v0.0.40: NEWEST known wins, not scanned-first). A stale
            // manually-extracted SDK folder in ~/Downloads used to shadow a newer cached
            // download because the scanned entry took priority unconditionally.
            var slEntry = result.Sources.FirstOrDefault(s => s.Source == "StreamlineSDK");
            var slScanned = slEntry != null && slEntry.Streamline != "Unknown" ? slEntry.Streamline : null;
            var slCached = _streamlineDownloadService.GetCachedSdkVersion();
            var slInstalled = slScanned != null && (slCached == null || !_versionComparer.IsNewer(slCached, slScanned))
                ? slScanned
                : slCached != null ? $"{slCached} (cached)" : null;

            // Latest Streamline upstream (v0.0.40: previously never queried during scan, so
            // the "UP TO DATE" pill ignored Streamline entirely — 2.11.1 showed green while
            // 2.12.0 sat on GitHub).
            string? slLatest = null;
            try
            {
                var slReleases = await _streamlineDownloadService.GetAvailableReleasesAsync();
                slLatest = slReleases
                    .Where(r => !string.IsNullOrEmpty(r.DownloadUrl))
                    .Select(r => r.Version)
                    .OrderBy(v => v, Comparer<string>.Create((a, b) =>
                        _versionComparer.IsNewer(a, b) ? 1 : _versionComparer.IsNewer(b, a) ? -1 : 0))
                    .LastOrDefault();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ScanAsync: could not query latest Streamline releases (offline?): {ex.Message}");
            }

            // v0.0.42: compare against the NEWEST known Streamline, same rule as the display.
            // slScanned ?? slCached let a stale ~/Downloads extract (a SOURCE folder that
            // never updates when Update All syncs) win the comparison — so "update available"
            // could never clear even after a successful 2.12.0 sync.
            var slBestKnown =
                slScanned != null && slCached != null
                    ? (_versionComparer.IsNewer(slCached, slScanned) ? slCached : slScanned)
                    : slScanned ?? slCached;
            // ponytail: slCached is a PROXY for "applied to NGX" — NGX folders carry no
            // Streamline version (their DLLs are 310.x DLSS-family), and Update All syncs
            // from exactly this zip. If a future sync path bypasses the cache, revisit.
            var slUpdateAvailable = slLatest != null &&
                (slBestKnown == null || _versionComparer.IsNewer(slLatest, slBestKnown));
            StreamlineVersion = slUpdateAvailable
                ? $"{slInstalled ?? "—"} → {slLatest}"
                : slInstalled ?? "—";

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

            // Determine the LATEST AVAILABLE version. Sources, in priority order:
            //   1. The newest version NVIDIA publishes on GitHub (the true "latest available").
            //   2. A cached SDK download already on disk.
            //   3. Fall back to the currently-installed NGX version — so when the user already
            //      has the newest DLLs and nothing newer exists upstream, the hero strip shows
            //      that version (not a blank "—") and correctly reads "UP TO DATE".
            // Comparison is numeric (VersionComparer), never lexical, so 310.6 < 310.10.
            var installedVer = (ngxRelease != null && ngxRelease.DLSS != "Unknown") ? ngxRelease.DLSS : null;
            var cachedVersion = _dlssDownloadService.GetCachedSdkVersion();

            string? latestAvailable = null;
            try
            {
                var releases = await _dlssDownloadService.GetAvailableReleasesAsync();
                latestAvailable = releases
                    .Select(r => r.Version)
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .OrderBy(v => v, Comparer<string>.Create((a, b) =>
                        _versionComparer.IsNewer(a, b) ? 1 : _versionComparer.IsNewer(b, a) ? -1 : 0))
                    .LastOrDefault();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ScanAsync: could not query latest DLSS releases (offline?): {ex.Message}");
            }

            // Take the newest of {upstream latest, cached, installed} as the displayed "available".
            foreach (var candidate in new[] { cachedVersion, installedVer })
            {
                if (!string.IsNullOrWhiteSpace(candidate) &&
                    (latestAvailable == null || _versionComparer.IsNewer(candidate!, latestAvailable)))
                {
                    latestAvailable = candidate;
                }
            }

            if (!string.IsNullOrWhiteSpace(latestAvailable))
            {
                // Display normalization (v0.0.40): GitHub tags are 3-part ("310.7.0") while
                // DLL FileVersionInfo is 4-part ("310.7.0.0"). They compare equal numerically;
                // pad the display so CURRENT and LATEST don't look mismatched.
                AvailableDlssVersion = PadVersionTo4(latestAvailable!);
                var dlssUpdate = installedVer == null || _versionComparer.IsNewer(latestAvailable!, installedVer);

                // v0.0.40: the pill now covers Streamline too — DLSS current while Streamline
                // is behind upstream means we are NOT up to date.
                UpdateAvailable = dlssUpdate || slUpdateAvailable;
                VersionStatusMessage =
                    dlssUpdate && slUpdateAvailable ? $"DLSS v{latestAvailable} + Streamline v{slLatest} available"
                    : dlssUpdate ? $"v{latestAvailable} available (current: {CurrentDlssVersion})"
                    : slUpdateAvailable ? $"Streamline v{slLatest} available (installed: {slBestKnown ?? "none"}) — run Update All"
                    : "Already up to date";
            }
            else
            {
                AvailableDlssVersion = "—";
                UpdateAvailable = slUpdateAvailable;
                VersionStatusMessage = slUpdateAvailable ? $"Streamline v{slLatest} available — run Update All" : "";
            }
            // Check AnWave detection — and now also reflect an EXISTING install (issue D).
            // The scan source tells us if AnWave was seen; DetectInstalled() reads the toolkit's
            // own install dir + actual DLL version so a prior Setup/Update All is recognised
            // instead of showing the amber "not set" dot with a blank version.
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

            // Reflect an already-installed AnWave (read-only disk probe).
            try
            {
                var anWaveInstall = _anWaveAutoService.DetectInstalled();
                if (anWaveInstall.IsInstalled)
                {
                    IsAnWaveInstalled = true;
                    AnWaveInstalledPath = anWaveInstall.InstalledPath ?? "";
                    AnWaveDllVersion = anWaveInstall.DllVersion ?? "";
                    AnWaveGlomVersion = anWaveInstall.GlomVersion ?? "";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ScanAsync: AnWave install detection failed: {ex.Message}");
            }

            // Reflect the real whitelist state (issue C) — read-only, no mutation.
            try
            {
                var whitelistState = await _whitelistService.DetectStateAsync();
                WhitelistStatus = whitelistState switch
                {
                    WhitelistState.Applied => "Applied",
                    WhitelistState.NotApplied => "Not applied",
                    _ => "N/A (NVIDIA App not found)"
                };
                IsWhitelistApplied = whitelistState == WhitelistState.Applied;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ScanAsync: whitelist state detection failed: {ex.Message}");
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
            RefreshGamesSection();
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

    // --- Games section (v0.0.47): profile names from the cached index, so the subject is
    //     the user's games, not the plumbing inventory. Staleness is disclosed, not hidden.
    public ObservableCollection<string> GameProfiles { get; } = new();
    public bool HasGames => GameProfiles.Count > 0;
    public string GamesFreshness { get; private set; } = "No profile index yet — run Index Game Profiles or Update All.";

    private void RefreshGamesSection()
    {
        var index = ProfileIndexStore.LoadRaw();
        GameProfiles.Clear();
        if (index == null || index.GameProfileNames.Count == 0)
        {
            GamesFreshness = "No profile index yet — run Index Game Profiles or Update All.";
            return;
        }
        foreach (var name in index.GameProfileNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).Take(40))
            GameProfiles.Add(name);
        GamesFreshness = $"{index.GameProfileNames.Count} game profile(s) · indexed {index.IndexedAt:yyyy-MM-dd HH:mm}";
    }

    // --- Run report drawer + backups (v0.0.47). The report manager lives on the VM so the
    //     drawer can bind its steps; Backups opens a dialog over the existing BackupService.
    public ObservableCollection<UpdateRunStep> RunSteps => _runReports.Steps;
    public bool HasRunSteps => _runReports.HasSteps;

    [RelayCommand]
    private void OpenBackups()
    {
        try
        {
            var ngxBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "NVIDIA", "NGX");
            var versionsParent = Path.Combine(ngxBase, NgxScanner.ReleaseSubPath);
            var dialog = new BackupsDialog(_backupService, versionsParent);
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open backups: {ex.Message}", "NGX Backups",
                MessageBoxButton.OK, MessageBoxImage.Error);
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
