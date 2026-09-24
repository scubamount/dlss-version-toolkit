namespace DLSSVersionToolkit.Core.Services;

using System.IO;
using System.Text.Json;
using System.Threading;
using DLSSVersionToolkit.Core.Models;

public interface ISettingsService
{
    Task<AppSettings> LoadAsync();
    Task SaveAsync(AppSettings settings);
    AppSettings GetCached();
}

public class SettingsService : ISettingsService
{
    private static readonly string SettingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DLSSVersionToolkit"
    );
    private static readonly string SettingsFile = Path.Combine(SettingsDirectory, "settings.json");

    /// <summary>
    /// Snapshot of the settings as they were when the Settings window was last opened (v0.76).
    /// A Save the user regrets can be undone with "Restore previous" in that window.
    /// </summary>
    public static string SnapshotFile => Path.Combine(SettingsDirectory, "settings.previous.json");
    private static readonly SemaphoreSlim _fileLock = new(1, 1);

    private AppSettings? _cachedSettings;

    public SettingsService()
    {
        EnsureSettingsDirectoryExists();
    }

    private static void EnsureSettingsDirectoryExists()
    {
        if (!Directory.Exists(SettingsDirectory))
        {
            Directory.CreateDirectory(SettingsDirectory);
        }
    }

    public async Task<AppSettings> LoadAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            if (!File.Exists(SettingsFile))
            {
                _cachedSettings = new AppSettings();
                await SaveInternalAsync(_cachedSettings);
                return _cachedSettings;
            }

            var json = await File.ReadAllTextAsync(SettingsFile);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            _cachedSettings = settings;
            return settings;
        }
        catch (JsonException)
        {
            _cachedSettings = new AppSettings();
            return _cachedSettings;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings)
    {
        await _fileLock.WaitAsync();
        try
        {
            await SaveInternalAsync(settings);
            _cachedSettings = settings;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static async Task SaveInternalAsync(AppSettings settings)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(settings, options);
        // Write-then-rename (v0.76): a crash or full disk mid-write used to leave a truncated
        // settings.json, which LoadAsync then treated as JsonException -> silent reset to defaults.
        EnsureSettingsDirectoryExists();
        var tmp = SettingsFile + ".tmp";
        await File.WriteAllTextAsync(tmp, json);
        File.Move(tmp, SettingsFile, overwrite: true);
    }

    /// <summary>Saves <paramref name="settings"/> as the "previous" snapshot. Best-effort.</summary>
    public static async Task WriteSnapshotAsync(AppSettings settings)
    {
        try
        {
            EnsureSettingsDirectoryExists();
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(SnapshotFile, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Settings snapshot failed (non-fatal): {ex.Message}");
        }
    }

    /// <summary>The "previous" snapshot, or null when none exists or it is unreadable.</summary>
    public static async Task<AppSettings?> ReadSnapshotAsync()
    {
        try
        {
            if (!File.Exists(SnapshotFile)) return null;
            return JsonSerializer.Deserialize<AppSettings>(await File.ReadAllTextAsync(SnapshotFile));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Settings snapshot unreadable: {ex.Message}");
            return null;
        }
    }

    public AppSettings GetCached()
    {
        return _cachedSettings ?? new AppSettings();
    }
}