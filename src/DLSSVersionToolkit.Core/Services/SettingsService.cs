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
        await File.WriteAllTextAsync(SettingsFile, json);
    }

    public AppSettings GetCached()
    {
        return _cachedSettings ?? new AppSettings();
    }
}