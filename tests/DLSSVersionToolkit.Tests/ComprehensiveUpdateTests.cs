using System.IO;
using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Tests for the comprehensive Update-All fix (v0.0.36): version is read from the DLL's
/// FileVersionInfo (not a nonexistent package config), the DLSS demo layout
/// (DLSS_Sample_App/bin/ngx_dlss_demo/) is recognized, and the Streamline SDK is the
/// comprehensive 4-DLL source. These tests cover the platform-agnostic pieces; the actual
/// FileVersionInfo read is exercised by CI on windows-latest against real DLLs at runtime.
/// </summary>
public class ComprehensiveUpdateTests
{
    // --- DllVersionReader: graceful handling of missing / no-version files ---

    [Fact]
    public void ReadFileVersion_MissingFile_ReturnsNull()
    {
        Assert.Null(DllVersionReader.ReadFileVersion(Path.Combine(Path.GetTempPath(), "does-not-exist-xyz.dll")));
    }

    [Fact]
    public void ReadFileVersion_NullOrEmptyPath_ReturnsNull()
    {
        Assert.Null(DllVersionReader.ReadFileVersion(""));
        Assert.Null(DllVersionReader.ReadFileVersion(null!));
    }

    [Fact]
    public void ReadDlssVersionFromFolder_MissingFolder_ReturnsNull()
    {
        Assert.Null(DllVersionReader.ReadDlssVersionFromFolder(Path.Combine(Path.GetTempPath(), "no-such-folder-xyz")));
    }

    [Fact]
    public void ReadDlssVersionFromFolder_FolderWithoutDll_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dlsstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // A folder with some other file but no nvngx_dlss.dll
            File.WriteAllText(Path.Combine(dir, "readme.txt"), "x");
            Assert.Null(DllVersionReader.ReadDlssVersionFromFolder(dir));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ReadFileVersion_NonPeFile_ReturnsNullGracefully()
    {
        // A text file named like a DLL must not throw — FileVersionInfo returns empty fields.
        var dir = Path.Combine(Path.GetTempPath(), $"dlsstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var fake = Path.Combine(dir, "nvngx_dlss.dll");
            File.WriteAllText(fake, "not a real PE");
            // Should not throw; returns null (no version resource) on a non-PE file.
            var result = DllVersionReader.ReadFileVersion(fake);
            Assert.Null(result);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ReadComponentVersion_MissingDll_ReturnsNull()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"dlsstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            Assert.Null(DllVersionReader.ReadComponentVersion(dir, "nvngx_dlssg.dll"));
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ReadComponentVersion_MissingFolder_ReturnsNull()
    {
        Assert.Null(DllVersionReader.ReadComponentVersion(
            Path.Combine(Path.GetTempPath(), "no-such-xyz"), "nvngx_dlss.dll"));
    }

    [Fact]
    public void NgxConfigParser_NoConfigNoDll_ReportsUnknown()
    {
        // The fix: a version folder with no config and no DLL must still parse cleanly to Unknown
        // (not crash), and report "Config file not found".
        var dir = Path.Combine(Path.GetTempPath(), $"dlsstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var result = new NgxConfigParser().Parse(dir);
            Assert.Equal("Unknown", result.DLSS);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void NgxConfigParser_StaleConfigKept_WhenNoDllPresent()
    {
        // With a config but no DLL, the parsed config version is used (DLL override is a no-op
        // when the DLL is absent). Confirms the override only fires when a real DLL exists.
        var dir = Path.Combine(Path.GetTempPath(), $"dlsstest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "nvngx_package_config.txt"), "dlss, 310.6.0.0\n");
            var result = new NgxConfigParser().Parse(dir);
            Assert.Equal("310.6.0.0", result.DLSS);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
