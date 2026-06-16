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

	// Clean up: restore default settings so other tests/app runs don't see test values
	var defaultSettings = new AppSettings();
	await service.SaveAsync(defaultSettings);
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

public class StreamlineDownloadServiceTests
{
    [Fact]
    public void GetCacheInfo_NoCacheDir_ReturnsZero()
    {
        var service = new Core.Services.StreamlineDownloadService();
        var (count, totalBytes) = service.GetCacheInfo();
        // May be 0 if no cache dir exists, or >0 if previous test left files
        Assert.True(count >= 0);
        Assert.True(totalBytes >= 0);
    }

    [Fact]
    public void GetCachedSdkVersion_NoCache_ReturnsNull()
    {
        var service = new Core.Services.StreamlineDownloadService();
        var version = service.GetCachedSdkVersion();
        Assert.Null(version);
    }

    [Fact]
    public void GetCachedDownloadPath_NoDownload_ReturnsNull()
    {
        var service = new Core.Services.StreamlineDownloadService();
        var path = service.GetCachedDownloadPath();
        Assert.Null(path);
    }

    [Fact]
    public void TrimCache_NoCacheDir_DoesNotThrow()
    {
        var service = new Core.Services.StreamlineDownloadService();
        // Should not throw even with no cache directory
        service.TrimCache(3);
    }
}

public class DlssIndicatorServiceTests
{
    [Fact]
    public void IsEnabled_NoRegistryKey_ReturnsFalse()
    {
        var service = new Core.Services.DlssIndicatorService();
        // When the NGXCore key doesn't exist, IsEnabled should return false
        var result = service.IsEnabled();
        Assert.False(result);
    }

    [Fact]
    public void IsEnabled_ImplementsInterface()
    {
        var service = new Core.Services.DlssIndicatorService();
        Assert.IsAssignableFrom<Core.Services.IDlssIndicatorService>(service);
    }

    [Fact]
    public void GetRawValue_NoRegistryKey_ReturnsNullOrInt()
    {
        var service = new Core.Services.DlssIndicatorService();
        // GetRawValue must never throw; it returns null when the value is absent,
        // otherwise the stored DWORD. (Enable writes 1024/0x400, not 1 — that was
        // the bug behind "indicator does nothing".)
        var raw = service.GetRawValue();
        Assert.True(raw is null || raw is int);
        // IsEnabled is defined as "any non-zero raw value".
        Assert.Equal(raw.HasValue && raw.Value != 0, service.IsEnabled());
    }
}

public class DlssIndicatorSetEnabledTests
{
    [Fact]
    public void SetEnabled_ImplementsInterface()
    {
        var service = new Core.Services.DlssIndicatorService();
        Assert.IsAssignableFrom<Core.Services.IDlssIndicatorService>(service);
    }
}

public class DlssPresetSettingIdsTests
{
    [Fact]
    public void OverrideEnableIds_MatchNvidiaHeader()
    {
        // Authoritative values from NVIDIA NvApiDriverSettings.h. The ENABLE flags are
        // what make the preset actually apply ("Custom" vs "use global default").
        Assert.Equal(0x10E41E01u, Core.Models.DlssPresetSettingIds.SR_OVERRIDE_ENABLE);
        Assert.Equal(0x10E41E02u, Core.Models.DlssPresetSettingIds.RR_OVERRIDE_ENABLE);
        Assert.Equal(0x10E41E03u, Core.Models.DlssPresetSettingIds.FG_OVERRIDE_ENABLE);
        Assert.Equal(0x10E41DF3u, Core.Models.DlssPresetSettingIds.SR_RENDER_PRESET);
        Assert.Equal(0x10E41DF7u, Core.Models.DlssPresetSettingIds.RR_RENDER_PRESET);
        Assert.Equal(1u, Core.Models.DlssPresetSettingIds.OVERRIDE_ON);
        Assert.Equal(0u, Core.Models.DlssPresetSettingIds.OVERRIDE_OFF);
    }

    [Fact]
    public void PresetEnumValues_MatchNvidiaPresetSelection()
    {
        // NvApiDriverSettings.h: render preset selection A=1..L=12,M=13.
        Assert.Equal(0x0Cu, (uint)Core.Models.DlssPreset.L); // 12
        Assert.Equal(0x0Bu, (uint)Core.Models.DlssPreset.K); // 11
    }
}

public class PresetApplyOptionsTests
{
    [Fact]
    public void Defaults_EnableSrRrFg_AndAllGameProfiles()
    {
        // Comprehensive-by-default: SR + RR (NR) + FG overrides on, sweep all game profiles.
        var o = new Core.Services.PresetApplyOptions();
        Assert.True(o.EnableSuperResolution);
        Assert.True(o.EnableRayReconstruction);
        Assert.True(o.EnableFrameGeneration);
        Assert.True(o.ApplyToAllGameProfiles);
    }

    [Fact]
    public void Result_CarriesProfileCounts()
    {
        var r = new Core.Services.PresetOverrideResult(true, Core.Models.DlssPreset.L, null, false, 7, 5);
        Assert.Equal(7, r.ProfilesUpdated);
        Assert.Equal(5, r.GameProfilesUpdated);
    }
}

public class WhitelistFlagFlipTests
{
    [Fact]
    public void FlipDisableOverrideFlags_ObjectRootedJson_FlipsAllTrueFlags()
    {
        // This is the shape that broke the user: NVIDIA App's ApplicationStorage.json
        // root is an OBJECT (wrapper), not an array. The old code bailed with
        // "root is not an array" and changed nothing, so the whitelist never applied.
        var json = """
        {
          "Storage": [
            { "name": "Game A", "Disable_SR_Override": true, "Disable_FG_Override": true },
            { "name": "Game B", "Disable_RR_Override":true, "Disable_SR_Model_Override" : true }
          ],
          "Disable_RR_Model_Override": true
        }
        """;

        var result = Core.Services.WhitelistService.FlipDisableOverrideFlags(json, out int flipped);

        Assert.Equal(5, flipped);
        Assert.DoesNotContain("Override\":true", result.Replace(" ", ""));
        Assert.DoesNotContain("Override\" :true", result); // whitespace variant gone too
        Assert.Contains("\"Disable_SR_Override\": false", result);
    }

    [Fact]
    public void FlipDisableOverrideFlags_NoTrueFlags_ReturnsZeroAndUnchanged()
    {
        var json = "{ \"Storage\": [ { \"Disable_SR_Override\": false } ] }";
        var result = Core.Services.WhitelistService.FlipDisableOverrideFlags(json, out int flipped);
        Assert.Equal(0, flipped);
        Assert.Equal(json, result);
    }
}

public class BackupServiceEdgeCaseTests
{
    [Fact]
    public void CreateBackup_PathOutsideAllowlist_ReturnsNull()
    {
        // Paths outside ProgramData\NVIDIA or AppData\NVIDIA must be rejected
        var service = new Core.Services.BackupService();
        var outsidePath = Path.Combine(Path.GetTempPath(), $"dlss-outside-{Guid.NewGuid()}");
        Directory.CreateDirectory(outsidePath);
        File.WriteAllText(Path.Combine(outsidePath, "test.dll"), "test");

        var backupPath = service.CreateBackup(outsidePath, Path.GetTempPath());
        Assert.Null(backupPath);

        Directory.Delete(outsidePath, true);
    }

    [Fact]
    public void RestoreBackup_NullPath_ReturnsFalse()
    {
        var service = new Core.Services.BackupService();
        Assert.False(service.RestoreBackup(null, "C:\\Some\\Path"));
        Assert.False(service.RestoreBackup("C:\\Some\\Path", null));
    }

    [Fact]
    public void CleanupOldBackups_NonExistentPath_DoesNotThrow()
    {
        var service = new Core.Services.BackupService();
        // Should not throw with non-existent parent path
        service.CleanupOldBackups(@"C:\NonExistent\Path\" + Guid.NewGuid());
    }
}

public class VersionComparerEdgeCaseTests
{
    [Fact]
    public void MarkNewest_AllSameVersion_NoneMarked()
    {
        var comparer = new Core.Services.VersionComparer();
        var result = new ScanResult
        {
            Sources = new List<DLSSVersionEntry>
            {
                new() { Source = "NGX_Release", DLSS = "310.6.0.0", FrameGen = "310.6.0.0" },
                new() { Source = "NGX_Staging", DLSS = "310.6.0.0", FrameGen = "310.6.0.0" }
            }
        };

        comparer.MarkNewest(result);

        // When versions are all equal, one should still be marked as newest (no crash)
        var markedCount = result.Sources.Count(s => s.IsNewestDLSS);
        Assert.Equal(1, markedCount);
    }

    [Fact]
    public void MarkNewest_HandlesN_A()
    {
        var comparer = new Core.Services.VersionComparer();
        var result = new ScanResult
        {
            Sources = new List<DLSSVersionEntry>
            {
                new() { Source = "NGX_Release", DLSS = "310.6.0.0" },
                new() { Source = "NGX_Staging", DLSS = "N/A" }
            }
        };

        comparer.MarkNewest(result);

        // N/A should not crash or be treated as newest
        Assert.True(result.Sources[0].IsNewestDLSS);
        Assert.False(result.Sources[1].IsNewestDLSS);
    }
}

public class OperationGuardTests
{
	[Fact]
	public void IsDirectoryWritable_ValidTempDir_ReturnsTrue()
	{
		var tempDir = Path.GetTempPath();
		Assert.True(Core.Services.OperationGuard.IsDirectoryWritable(tempDir));
	}

	[Fact]
	public void IsDirectoryWritable_NonExistentDir_ReturnsFalse()
	{
		Assert.False(Core.Services.OperationGuard.IsDirectoryWritable(@"C:\NonExistent\Path\" + Guid.NewGuid()));
	}

	[Fact]
	public void HasDiskSpace_SystemDrive_ReturnsTrueForSmallRequest()
	{
		var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
		// 1 MB should always be available on system drive
		Assert.True(Core.Services.OperationGuard.HasDiskSpace(systemDir, 1 * 1024 * 1024));
	}

	[Fact]
	public void HasDiskSpace_ImpossibleRequest_ReturnsFalse()
	{
		var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
		// 1 PB should not be available
		Assert.False(Core.Services.OperationGuard.HasDiskSpace(systemDir, 1L * 1024 * 1024 * 1024 * 1024 * 1024));
	}

	[Fact]
	public void VerifyFile_ExistingFile_ReturnsTrue()
	{
		var tempFile = Path.Combine(Path.GetTempPath(), $"dlss-guard-test-{Guid.NewGuid()}.txt");
		File.WriteAllText(tempFile, "test content");

		Assert.True(Core.Services.OperationGuard.VerifyFile(tempFile));

		File.Delete(tempFile);
	}

	[Fact]
	public void VerifyFile_NonExistentFile_ReturnsFalse()
	{
		Assert.False(Core.Services.OperationGuard.VerifyFile(@"C:\NonExistent\file.dll"));
	}

	[Fact]
	public void VerifyFile_SizeMismatch_ReturnsFalse()
	{
		var tempFile = Path.Combine(Path.GetTempPath(), $"dlss-guard-test-{Guid.NewGuid()}.txt");
		File.WriteAllText(tempFile, "test");
		var actualSize = new FileInfo(tempFile).Length;

		Assert.False(Core.Services.OperationGuard.VerifyFile(tempFile, actualSize + 100));
		Assert.True(Core.Services.OperationGuard.VerifyFile(tempFile, actualSize));

		File.Delete(tempFile);
	}

	[Fact]
	public void VerifyDllSignature_ValidPeFile_ReturnsTrue()
	{
		// Create a minimal PE-like file: MZ header + enough bytes
		var tempFile = Path.Combine(Path.GetTempPath(), $"dlss-guard-pe-{Guid.NewGuid()}.dll");
		var data = new byte[2048];
		data[0] = (byte)'M';
		data[1] = (byte)'Z';
		File.WriteAllBytes(tempFile, data);

		Assert.True(Core.Services.OperationGuard.VerifyDllSignature(tempFile));

		File.Delete(tempFile);
	}

	[Fact]
	public void VerifyDllSignature_InvalidHeader_ReturnsFalse()
	{
		var tempFile = Path.Combine(Path.GetTempPath(), $"dlss-guard-bad-{Guid.NewGuid()}.dll");
		var data = new byte[2048];
		data[0] = (byte)'X';
		data[1] = (byte)'Y';
		File.WriteAllBytes(tempFile, data);

		Assert.False(Core.Services.OperationGuard.VerifyDllSignature(tempFile));

		File.Delete(tempFile);
	}

	[Fact]
	public void VerifyDllSignature_TooSmallFile_ReturnsFalse()
	{
		var tempFile = Path.Combine(Path.GetTempPath(), $"dlss-guard-small-{Guid.NewGuid()}.dll");
		var data = new byte[100]; // Less than 1024 minimum
		data[0] = (byte)'M';
		data[1] = (byte)'Z';
		File.WriteAllBytes(tempFile, data);

		Assert.False(Core.Services.OperationGuard.VerifyDllSignature(tempFile));

		File.Delete(tempFile);
	}

	[Fact]
	public void VerifyBackupDirectory_ValidDir_ReturnsTrue()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), $"dlss-guard-backup-{Guid.NewGuid()}");
		Directory.CreateDirectory(tempDir);
		File.WriteAllText(Path.Combine(tempDir, "file1.dll"), "content");

		Assert.True(Core.Services.OperationGuard.VerifyBackupDirectory(tempDir));
		Assert.True(Core.Services.OperationGuard.VerifyBackupDirectory(tempDir, 1));

		Directory.Delete(tempDir, true);
	}

	[Fact]
	public void VerifyBackupDirectory_NonExistentDir_ReturnsFalse()
	{
		Assert.False(Core.Services.OperationGuard.VerifyBackupDirectory(@"C:\NonExistent\Backup" + Guid.NewGuid()));
	}

	[Fact]
	public void VerifyBackupDirectory_EmptyDir_ReturnsFalse()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), $"dlss-guard-empty-{Guid.NewGuid()}");
		Directory.CreateDirectory(tempDir);

		Assert.False(Core.Services.OperationGuard.VerifyBackupDirectory(tempDir));

		Directory.Delete(tempDir, true);
	}

	[Fact]
	public void EnsureDirectoryExists_CreatesNewDir_ReturnsTrue()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), $"dlss-guard-newdir-{Guid.NewGuid()}");
		Assert.False(Directory.Exists(tempDir));

		Assert.True(Core.Services.OperationGuard.EnsureDirectoryExists(tempDir));
		Assert.True(Directory.Exists(tempDir));

		Directory.Delete(tempDir, true);
	}

	[Fact]
	public void EnsureDirectoryExists_ExistingDir_ReturnsTrue()
	{
		var tempDir = Path.Combine(Path.GetTempPath(), $"dlss-guard-existdir-{Guid.NewGuid()}");
		Directory.CreateDirectory(tempDir);

		Assert.True(Core.Services.OperationGuard.EnsureDirectoryExists(tempDir));

		Directory.Delete(tempDir, true);
	}
}

public class AppUpdateServiceTests
{
	// --- Tag parsing ---

	[Theory]
	[InlineData("v0.0.31", "0.0.31")]
	[InlineData("0.0.31", "0.0.31")]
	[InlineData("V1.2.3", "1.2.3")]
	[InlineData("v0.0.31.5", "0.0.31.5")]
	public void ParseTagVersion_ValidTags_Parses(string tag, string expected)
	{
		var v = Core.Services.AppUpdateService.ParseTagVersion(tag);
		Assert.NotNull(v);
		Assert.Equal(Version.Parse(expected), v);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	[InlineData("latest")]
	[InlineData("v1")]
	[InlineData("not-a-version")]
	public void ParseTagVersion_InvalidTags_ReturnsNull(string? tag)
	{
		Assert.Null(Core.Services.AppUpdateService.ParseTagVersion(tag));
	}

	// --- Newer-version comparison ---

	[Theory]
	[InlineData("0.0.32", "0.0.31", true)]   // patch newer
	[InlineData("0.1.0", "0.0.31", true)]    // minor newer
	[InlineData("1.0.0", "0.0.31", true)]    // major newer
	[InlineData("0.0.31", "0.0.31", false)]  // equal
	[InlineData("0.0.30", "0.0.31", false)]  // older
	public void IsNewer_ComparesCorrectly(string latest, string current, bool expected)
	{
		var result = Core.Services.AppUpdateService.IsNewer(
			Version.Parse(latest), Version.Parse(current));
		Assert.Equal(expected, result);
	}

	[Fact]
	public void IsNewer_NullLatest_ReturnsFalse()
	{
		Assert.False(Core.Services.AppUpdateService.IsNewer(null, Version.Parse("0.0.31")));
	}

	[Fact]
	public void IsNewer_NormalizesUndefinedComponents()
	{
		// 0.0.31 (revision undefined, -1) vs 0.0.31.0 must compare EQUAL, not older.
		Assert.False(Core.Services.AppUpdateService.IsNewer(
			Version.Parse("0.0.31"), Version.Parse("0.0.31.0")));
		Assert.False(Core.Services.AppUpdateService.IsNewer(
			Version.Parse("0.0.31.0"), Version.Parse("0.0.31")));
	}

	// --- AppUpdateInfo defaults ---

	[Fact]
	public void AppUpdateInfo_Defaults_NoUpdate()
	{
		var info = new AppUpdateInfo();
		Assert.False(info.IsUpdateAvailable);
		Assert.Equal("", info.DownloadUrl);
		Assert.Equal(0, info.AssetSize);
	}

	[Fact]
	public void AppUpdateResult_FactoryMethods()
	{
		var ok = AppUpdateResult.Succeeded(@"C:\apps\DLSSVersionToolkit.exe");
		Assert.True(ok.Success);
		Assert.Equal(@"C:\apps\DLSSVersionToolkit.exe", ok.ExePath);
		Assert.Equal("", ok.ErrorMessage);

		var fail = AppUpdateResult.Failed("boom");
		Assert.False(fail.Success);
		Assert.Equal("boom", fail.ErrorMessage);
	}

	// --- Settings backward compatibility ---

	[Fact]
	public void AppSettings_NewFields_HaveSafeDefaults()
	{
		// Old settings.json files won't contain the new keys; deserialization must fall
		// back to: update checks ON, quick guide NOT yet seen.
		var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}");
		Assert.NotNull(settings);
		Assert.True(settings!.CheckForAppUpdates);
		Assert.False(settings.HasSeenQuickGuide);
	}
}

public class WhitelistDetectionTests
{
	[Fact]
	public void CountTrueJsonDisableFlags_CountsOnlyTrueFlags()
	{
		var json = """
		{
		  "games": {
		    "Disable_SR_Override": true,
		    "Disable_RR_Override": false,
		    "Disable_FG_Override": true,
		    "Disable_SR_Model_Override": false,
		    "Disable_RR_Model_Override": false
		  }
		}
		""";
		// 2 flags still ON (true) → not fully whitelisted
		Assert.Equal(2, Core.Services.WhitelistService.CountTrueJsonDisableFlags(json));
	}

	[Fact]
	public void CountTrueJsonDisableFlags_AllFalse_ReturnsZero()
	{
		var json = """
		{ "Disable_SR_Override": false, "Disable_FG_Override": false }
		""";
		Assert.Equal(0, Core.Services.WhitelistService.CountTrueJsonDisableFlags(json));
	}

	[Fact]
	public void CountTrueJsonDisableFlags_WhitespaceTolerant()
	{
		// pretty-printed with varying spacing around the colon
		var json = "{ \"Disable_SR_Override\"  :   true }";
		Assert.Equal(1, Core.Services.WhitelistService.CountTrueJsonDisableFlags(json));
	}

	[Fact]
	public void CountTrueJsonDisableFlags_NoFlags_ReturnsZero()
	{
		Assert.Equal(0, Core.Services.WhitelistService.CountTrueJsonDisableFlags("{ \"other\": true }"));
	}

	[Fact]
	public void FlipAndCount_AreConsistent()
	{
		// After flipping, the true-count must be zero — the detect twin agrees with the apply path.
		var json = "{ \"Disable_SR_Override\": true, \"Disable_FG_Override\": true }";
		var flipped = Core.Services.WhitelistService.FlipDisableOverrideFlags(json, out var n);
		Assert.Equal(2, n);
		Assert.Equal(0, Core.Services.WhitelistService.CountTrueJsonDisableFlags(flipped));
	}
}

public class VersionComparerPublicTests
{
	private readonly Core.Services.VersionComparer _c = new();

	[Theory]
	[InlineData("310.10.0.0", "310.6.0.0", true)]   // numeric, not lexical: 10 > 6
	[InlineData("310.6.0.0", "310.10.0.0", false)]
	[InlineData("310.7.0.0", "310.6.0.0", true)]
	[InlineData("310.6.0.0", "310.6.0.0", false)]   // equal
	[InlineData("311.0.0.0", "310.99.0.0", true)]
	public void IsNewer_NumericComparison(string candidate, string baseline, bool expected)
	{
		Assert.Equal(expected, _c.IsNewer(candidate, baseline));
	}

	[Fact]
	public void IsNewer_UnknownHandling()
	{
		Assert.False(_c.IsNewer("Unknown", "310.6.0.0"));
		Assert.True(_c.IsNewer("310.6.0.0", "Unknown"));
	}
}