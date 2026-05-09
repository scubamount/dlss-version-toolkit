using DLSSVersionToolkit.Core.Models;

namespace DLSSVersionToolkit.Tests;

public class VersionComparerTests
{
    [Fact]
    public void MarkNewest_MarksHighestVersion()
    {
        var comparer = new Core.Services.VersionComparer();
        var result = new ScanResult
        {
            Sources = new List<DLSSVersionEntry>
            {
                new() { Source = "NGX_Release", DLSS = "310.6.0.0", FrameGen = "310.6.0.0" },
                new() { Source = "NGX_Staging", DLSS = "310.7.0.0", FrameGen = "310.7.0.0" }
            }
        };

        comparer.MarkNewest(result);

        Assert.False(result.Sources[0].IsNewestDLSS);
        Assert.True(result.Sources[1].IsNewestDLSS);
        Assert.Equal("310.7.0.0", result.NewestPerComponent["DLSS"].Version);
    }

    [Fact]
    public void MarkNewest_HandlesUnknown()
    {
        var comparer = new Core.Services.VersionComparer();
        var result = new ScanResult
        {
            Sources = new List<DLSSVersionEntry>
            {
                new() { Source = "NGX_Release", DLSS = "Unknown", FrameGen = "Unknown" },
                new() { Source = "NGX_Staging", DLSS = "310.7.0.0", FrameGen = "310.7.0.0" }
            }
        };

        comparer.MarkNewest(result);

        Assert.True(result.Sources[1].IsNewestDLSS);
    }

    [Fact]
    public void GenerateRecommendations_UpdatesFromStreamline()
    {
        var comparer = new Core.Services.VersionComparer();
        var result = new ScanResult
        {
            Sources = new List<DLSSVersionEntry>
            {
                new() { Source = "NGX_Release", DLSS = "310.6.0.0" },
                new() { Source = "StreamlineSDK", DLSS = "310.7.0.0" }
            },
            NewestPerComponent = new Dictionary<string, VersionInfo>
            {
                { "DLSS", new VersionInfo("310.7.0.0", "StreamlineSDK") }
            }
        };

        var recs = comparer.GenerateRecommendations(result);

        Assert.Single(recs);
        Assert.Equal("Update_NGX_from_StreamlineSDK", recs[0].Action);
    }

    [Fact]
    public void GenerateRecommendations_AllUpToDate()
    {
        var comparer = new Core.Services.VersionComparer();
        var result = new ScanResult
        {
            Sources = new List<DLSSVersionEntry>
            {
                new() { Source = "NGX_Release", DLSS = "310.7.0.0" },
                new() { Source = "StreamlineSDK", DLSS = "310.6.0.0" }
            },
            NewestPerComponent = new Dictionary<string, VersionInfo>
            {
                { "DLSS", new VersionInfo("310.7.0.0", "NGX_Release") }
            }
        };

        var recs = comparer.GenerateRecommendations(result);

        Assert.Single(recs);
        Assert.Equal("UpToDate", recs[0].Action);
    }
}

public class NgxConfigParserTests
{
    private readonly Core.Services.NgxConfigParser _parser;

    public NgxConfigParserTests()
    {
        _parser = new Core.Services.NgxConfigParser();
    }

    [Fact]
    public void Parse_ValidConfig_ReturnsVersions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dlss-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "nvngx_package_config.txt"), "dlss, 310.6.0.0\ndlssg, 310.6.0.0\ndlssd, 310.6.0.0\ndeepdvc, 310.6.0.0");

        var result = _parser.Parse(tempDir);

        Assert.Equal("310.6.0.0", result.DLSS);
        Assert.Equal("310.6.0.0", result.FrameGen);
        Assert.Equal("310.6.0.0", result.DLSSD);
        Assert.Equal("310.6.0.0", result.DeepDVC);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Parse_MissingComponent_ReturnsUnknown()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dlss-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "nvngx_package_config.txt"), "dlss, 310.6.0.0");

        var result = _parser.Parse(tempDir);

        Assert.Equal("310.6.0.0", result.DLSS);
        Assert.Equal("Unknown", result.FrameGen);
        Assert.Equal("Unknown", result.DLSSD);
        Assert.Equal("Unknown", result.DeepDVC);

        Directory.Delete(tempDir, true);
    }

    [Fact]
    public void Parse_NonExistentFolder_ReturnsMessage()
    {
        var result = _parser.Parse(@"C:\NonExistentFolder");
        Assert.Equal("Folder not found", result.Message);
    }

    [Fact]
    public void Parse_EmptyConfig_ReturnsMessage()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"dlss-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);
        File.WriteAllText(Path.Combine(tempDir, "nvngx_package_config.txt"), "");

        var result = _parser.Parse(tempDir);
        Assert.Equal("Config file empty", result.Message);

        Directory.Delete(tempDir, true);
    }
}

public class NgxScannerTests
{
    [Fact]
    public void Scan_NoNGXPath_ReturnsEmpty()
    {
        var parser = new Core.Services.NgxConfigParser();
        var scanner = new Core.Services.NgxScanner(parser);

        var results = scanner.Scan(@"C:\NonExistent");

        Assert.Empty(results);
    }
}

public class BackupServiceTests
{
    [Fact]
    public void CreateBackup_ValidFolder_CreatesBackup()
    {
        var service = new Core.Services.BackupService();

        // BackupService validates that parent path is under ProgramData\NVIDIA or AppData\NVIDIA
        var nvidiaParent = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "NVIDIA", "NGX", "models", "dlss_override", "versions");
        var sourceDir = Path.Combine(nvidiaParent, $"dlss-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "test.dll"), "test");

        var backupPath = service.CreateBackup(sourceDir, nvidiaParent);

        Assert.NotNull(backupPath);
        Assert.True(Directory.Exists(backupPath));
        Assert.Contains(".dlss-backup-", Path.GetFileName(backupPath));

        Directory.Delete(sourceDir, true);
        if (Directory.Exists(backupPath)) Directory.Delete(backupPath, true);
        // Clean up test NVIDIA directory tree if empty
        try { Directory.Delete(nvidiaParent, false); } catch { }
        try { Directory.Delete(Path.GetDirectoryName(nvidiaParent)!, false); } catch { }
        try { Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(nvidiaParent)!)!, false); } catch { }
    }

    [Fact]
    public void CreateBackup_NonExistentFolder_ReturnsNull()
    {
        var service = new Core.Services.BackupService();
        var backupPath = service.CreateBackup(@"C:\NonExistent", Path.GetTempPath());
        Assert.Null(backupPath);
    }
}

public class SettingsServiceTests
{
    [Fact]
    public async Task LoadAsync_DefaultSettings_ReturnsDefaultPaths()
    {
        // Ensure no leftover settings file from other tests
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DLSSVersionToolkit");
        var settingsFile = Path.Combine(settingsDir, "settings.json");
        if (File.Exists(settingsFile))
        {
            try { File.Delete(settingsFile); } catch { }
        }

        var service = new Core.Services.SettingsService();
        var settings = await service.LoadAsync();

        Assert.Equal("", settings.NgxBasePath); // Empty by default — auto-detected at runtime
        Assert.False(settings.AutoScanEnabled);
        Assert.Equal(4, settings.ScanIntervalHours);
    }

    [Fact]
    public async Task SaveAndLoad_CustomSettings_Persisted()
    {
        var service = new Core.Services.SettingsService();
        var customSettings = new AppSettings
        {
            NgxBasePath = @"C:\Custom\Path",
            AnWavePath = @"C:\AnWave",
            StreamlinePath = @"C:\Streamline"
        };

        await service.SaveAsync(customSettings);
        var loaded = await service.LoadAsync();

        Assert.Equal(@"C:\Custom\Path", loaded.NgxBasePath);
        Assert.Equal(@"C:\AnWave", loaded.AnWavePath);
        Assert.Equal(@"C:\Streamline", loaded.StreamlinePath);
    }
}

public class ExportServiceTests
{
    [Fact]
    public void ExportToCsv_CreatesValidFile()
    {
        var service = new Core.Services.ExportService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"dlss-export-{Guid.NewGuid()}.csv");

        var result = new ScanResult
        {
            Sources = new List<DLSSVersionEntry>
            {
                new() { Source = "NGX_Release", BuildID = "310.6", DLSS = "310.6.0.0" }
            },
            ScannedAt = DateTime.UtcNow
        };

        service.ExportToCsv(result, tempFile);

        Assert.True(File.Exists(tempFile));
        var content = File.ReadAllText(tempFile);
        Assert.Contains("Source,BuildID", content);
        Assert.Contains("NGX_Release", content);

        File.Delete(tempFile);
    }

    [Fact]
    public void ExportToJson_CreatesValidFile()
    {
        var service = new Core.Services.ExportService();
        var tempFile = Path.Combine(Path.GetTempPath(), $"dlss-export-{Guid.NewGuid()}.json");

        var result = new ScanResult
        {
            Sources = new List<DLSSVersionEntry>
            {
                new() { Source = "NGX_Release", DLSS = "310.6.0.0" }
            }
        };

        service.ExportToJson(result, tempFile);

        Assert.True(File.Exists(tempFile));
        var content = File.ReadAllText(tempFile);
        Assert.Contains("NGX_Release", content);

        File.Delete(tempFile);
    }
}

public class GlobalScannerTests
{
    [Fact]
    public void Scan_NonExistentPath_ReturnsNull()
    {
        var scanner = new Core.Services.GlobalScanner();
        var result = scanner.Scan(@"C:\NonExistent");
        Assert.Null(result);
    }

    [Fact]
    public void Scan_EmptyPath_ReturnsNull()
    {
        var scanner = new Core.Services.GlobalScanner();
        var result = scanner.Scan("");
        Assert.Null(result);
    }
}

public class StreamlineScannerTests
{
    [Fact]
    public void Scan_EmptyPath_ReturnsNull()
    {
        var scanner = new Core.Services.StreamlineScanner();
        var result = scanner.Scan("");
        Assert.Null(result);
    }

    [Fact]
    public void AutoDetectInDownloads_NoStreamline_ReturnsNull()
    {
        var scanner = new Core.Services.StreamlineScanner();
        var result = scanner.AutoDetectInDownloads();
        // Just verify it doesn't throw - may or may not find anything
        Assert.True(result == null || Directory.Exists(result));
    }
}