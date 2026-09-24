namespace DLSSVersionToolkit.Core.Services;

using System.Text.Json;

/// <summary>
/// What the machine looked like before this app changed anything (v0.76). Captured once, at the
/// first launch that finds no baseline, and never overwritten — Reset restores THIS, not "empty".
/// </summary>
public sealed class OverrideBaseline
{
    public DateTime CapturedAt { get; set; }

    /// <summary>Full text of nvngx_config.txt at capture, or null when the file did not exist.</summary>
    public string? NgxConfigText { get; set; }

    /// <summary>Raw ShowDlssIndicator DWORD at capture, or null when the value was absent.</summary>
    public int? IndicatorRawValue { get; set; }
}

/// <summary>One line of the Reset report: what was undone, or why it could not be.</summary>
public sealed record ResetStep(string Name, bool Success, string Detail);

public sealed class ResetResult
{
    public List<ResetStep> Steps { get; } = new();
    public string? BackupFolder { get; set; }
    public bool AllSucceeded => Steps.All(s => s.Success);
}

/// <summary>
/// Undoes the global overrides this app applies (v0.76). The "↺ Reset" button before v0.76 only
/// reset dropdown selections, so a user who wanted the driver back to stock had no path short of
/// hand-editing nvngx_config.txt and the DRS profiles.
///
/// Scope — what Reset DOES undo:
///   • nvngx_config.txt: restored to the pre-toolkit baseline text, or deleted when there was none.
///     The copy being replaced is backed up first.
///   • DLSS on-screen indicator: restored to the baseline registry value.
///   • Toolkit override records (overrides.json): cleared, so no row keeps claiming 🔒.
/// The driver preset overrides (DRS) are handled by the caller through IPresetOverrideService,
/// which already writes "override off" for the Default preset.
///
/// What Reset deliberately does NOT touch:
///   • DLL files in the NGX tree. NVIDIA's own updater owns that tree and re-fills it; deleting
///     driver-managed files is destructive and not reversible by this app. The version-folder
///     backups remain available under Backups.
///   • The NVIDIA App whitelist edits (ApplicationStorage.json / fingerprint.db). Those carry
///     their own .bak and are re-written by the NVIDIA App on update.
/// </summary>
public sealed class OverrideResetService
{
    public const string BaselineFileName = "override-baseline.json";

    private readonly string _appDataRoot;
    private readonly string _configPath;
    private readonly IOverrideManifestService _manifest;

    public OverrideResetService(IOverrideManifestService manifest, string? appDataRoot = null, string? configPath = null)
    {
        _manifest = manifest;
        _appDataRoot = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DLSSVersionToolkit");
        _configPath = configPath ?? NgxPathResolver.GetConfigFilePath();
    }

    public string BaselinePath => Path.Combine(_appDataRoot, BaselineFileName);

    /// <summary>The saved baseline, or null when none has been captured.</summary>
    public OverrideBaseline? LoadBaseline()
    {
        try
        {
            if (!File.Exists(BaselinePath)) return null;
            return JsonSerializer.Deserialize<OverrideBaseline>(File.ReadAllText(BaselinePath));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"OverrideResetService: baseline unreadable: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Captures the baseline if none exists. Idempotent: an existing baseline is never replaced,
    /// because a second capture would record the toolkit's own changes as "original".
    ///
    /// A config that already carries this app's exact override block is NOT captured as original
    /// (the user ran a pre-v0.76 build before any baseline existed); the baseline then records
    /// "no config", which is the state before any toolkit write.
    /// </summary>
    public bool EnsureBaselineCaptured(int? indicatorRawValue)
    {
        if (File.Exists(BaselinePath)) return false;

        string? configText = null;
        try
        {
            if (File.Exists(_configPath))
                configText = File.ReadAllText(_configPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable config: do not write a baseline that would claim "no config existed".
            System.Diagnostics.Debug.WriteLine($"OverrideResetService: cannot read config for baseline: {ex.Message}");
            return false;
        }

        if (configText != null && LooksToolkitWritten(configText))
            configText = null;

        var baseline = new OverrideBaseline
        {
            CapturedAt = DateTime.UtcNow,
            NgxConfigText = configText,
            // 1024 is the value this app writes; treat it as toolkit state, not original.
            IndicatorRawValue = indicatorRawValue == 1024 ? null : indicatorRawValue,
        };

        Directory.CreateDirectory(_appDataRoot);
        File.WriteAllText(BaselinePath, JsonSerializer.Serialize(baseline, new JsonSerializerOptions { WriteIndented = true }));
        return true;
    }

    /// <summary>True for a config whose [dlss_override] block is the one AnWaveAutoService writes.</summary>
    public static bool LooksToolkitWritten(string configText) =>
        configText.Contains("[dlss_override]", StringComparison.OrdinalIgnoreCase) &&
        configText.Contains("app_E658700_force = 1", StringComparison.OrdinalIgnoreCase) &&
        configText.Contains("[streamline_override]", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Restores nvngx_config.txt to the baseline and clears override records. The indicator is
    /// returned to the caller (registry access is Windows-only and lives in IDlssIndicatorService).
    /// </summary>
    public ResetResult ResetFiles()
    {
        var result = new ResetResult();
        var baseline = LoadBaseline();

        // Back up the current config before any change — Reset must itself be undoable.
        var backupDir = Path.Combine(_appDataRoot, "ResetBackups", DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        try
        {
            if (File.Exists(_configPath))
            {
                Directory.CreateDirectory(backupDir);
                File.Copy(_configPath, Path.Combine(backupDir, Path.GetFileName(_configPath)), overwrite: true);
                result.BackupFolder = backupDir;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Steps.Add(new ResetStep("Override config", false,
                $"could not back up nvngx_config.txt, so it was left unchanged: {ex.Message}"));
            ClearManifest(result);
            return result;
        }

        try
        {
            if (baseline?.NgxConfigText is { } original)
            {
                File.WriteAllText(_configPath, original);
                result.Steps.Add(new ResetStep("Override config", true,
                    "nvngx_config.txt restored to the version from before this app changed it"));
            }
            else if (File.Exists(_configPath))
            {
                File.Delete(_configPath);
                result.Steps.Add(new ResetStep("Override config", true,
                    "nvngx_config.txt removed (it did not exist before this app created it)"));
            }
            else
            {
                result.Steps.Add(new ResetStep("Override config", true, "no override config present"));
            }
        }
        catch (UnauthorizedAccessException)
        {
            result.Steps.Add(new ResetStep("Override config", false,
                "Administrator access is required to change nvngx_config.txt. Restart the app as Administrator."));
        }
        catch (IOException ex)
        {
            result.Steps.Add(new ResetStep("Override config", false, ex.Message));
        }

        ClearManifest(result);
        return result;
    }

    private void ClearManifest(ResetResult result)
    {
        try
        {
            var manifest = _manifest.Load();
            var count = manifest.Overrides.Count;
            if (count > 0)
            {
                manifest.Overrides.Clear();
                _manifest.Save(manifest);
            }
            result.Steps.Add(new ResetStep("Override records", true,
                count > 0 ? $"{count} imported-override record(s) cleared" : "no imported overrides recorded"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Steps.Add(new ResetStep("Override records", false, ex.Message));
        }
    }
}
