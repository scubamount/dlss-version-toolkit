using DLSSVersionToolkit.Core.Models;
using DLSSVersionToolkit.Core.Services;

namespace DLSSVersionToolkit.Tests;

/// <summary>
/// Pins the override manifest and, most importantly, the decision Update All makes about each
/// recorded override (v0.0.52).
///
/// The dangerous case is SUPERSEDED. Re-asserting an override unconditionally is a silent
/// DOWNGRADE the day NVIDIA publishes something newer than the manually imported DLL, and nothing
/// in the UI would ever say so. Two of these tests are red arms: they fail against the naive
/// "always re-assert" implementation.
/// </summary>
public class OverrideManifestTests
{
    private static (OverrideManifestService svc, string root) NewService()
    {
        var root = Path.Combine(Path.GetTempPath(), "dlss-ovr-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        return (new OverrideManifestService(new VersionComparer(), root), root);
    }

    private static string WriteFakeDll(string dir, string name, string content = "MZfake")
    {
        Directory.CreateDirectory(dir);
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void EmptyManifest_LoadsCleanly()
    {
        var (svc, root) = NewService();
        try
        {
            var m = svc.Load();
            Assert.NotNull(m);
            Assert.Empty(m.Overrides);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RecordImport_RoundTrips()
    {
        var (svc, root) = NewService();
        try
        {
            var dll = WriteFakeDll(root, "nvngx_dlssd.dll");
            svc.RecordImport("nvngx_dlssd.dll", "310.7.128.0", dll, "20318080", staging: true);

            var m = svc.Load();
            var rec = Assert.Single(m.Overrides);
            Assert.Equal("nvngx_dlssd.dll", rec.DllName);
            Assert.Equal("310.7.128.0", rec.Version);
            Assert.Equal("20318080", rec.PackedFolder);
            Assert.True(rec.Staging);
            Assert.False(string.IsNullOrWhiteSpace(rec.Sha256));
            Assert.Equal("310.7.128.0", svc.GetOverrideVersion("nvngx_dlssd.dll"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void RecordImport_ReplacesPriorRecordForSameDll()
    {
        var (svc, root) = NewService();
        try
        {
            var dll = WriteFakeDll(root, "nvngx_dlssd.dll");
            svc.RecordImport("nvngx_dlssd.dll", "310.7.0.0", dll, "20317952", false);
            svc.RecordImport("nvngx_dlssd.dll", "310.7.128.0", dll, "20318080", false);

            var rec = Assert.Single(svc.Load().Overrides);
            Assert.Equal("310.7.128.0", rec.Version);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Remove_DropsTheRecord()
    {
        var (svc, root) = NewService();
        try
        {
            var dll = WriteFakeDll(root, "nvngx_dlssd.dll");
            svc.RecordImport("nvngx_dlssd.dll", "310.7.128.0", dll, "20318080", false);
            svc.Remove("nvngx_dlssd.dll");
            Assert.Empty(svc.Load().Overrides);
            Assert.Null(svc.GetOverrideVersion("nvngx_dlssd.dll"));
        }
        finally { Directory.Delete(root, true); }
    }

    // --- The disposition decision -------------------------------------------------------------

    [Fact]
    public void ChannelNewerThanOverride_IsSuperseded_NotReasserted()
    {
        // RED ARM. A naive implementation re-asserts whenever the bytes no longer match, which
        // would reinstall 310.7.128 over a freshly downloaded 310.8 — a silent downgrade.
        var (svc, root) = NewService();
        try
        {
            var dll = WriteFakeDll(root, "nvngx_dlssd.dll");
            svc.RecordImport("nvngx_dlssd.dll", "310.7.128.0", dll, "20318080", false);

            var statuses = svc.Evaluate(
                installedByDll: new Dictionary<string, string?>(),
                channelByDll: new Dictionary<string, string?> { ["nvngx_dlssd.dll"] = "310.8.0.0" });

            var s = Assert.Single(statuses);
            Assert.Equal(OverrideDisposition.Superseded, s.Disposition);
            Assert.Contains("310.8.0.0", s.Explanation);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ChannelOlderThanOverride_IsNotSuperseded()
    {
        // Andrew's real situation: 4.5 RR imported, NVIDIA still publishing 310.7.0.
        var (svc, root) = NewService();
        try
        {
            var dll = WriteFakeDll(root, "nvngx_dlssd.dll");
            svc.RecordImport("nvngx_dlssd.dll", "310.7.128.0", dll, "20318080", false);

            var statuses = svc.Evaluate(
                new Dictionary<string, string?>(),
                new Dictionary<string, string?> { ["nvngx_dlssd.dll"] = "310.7.0.0" });

            Assert.NotEqual(OverrideDisposition.Superseded, Assert.Single(statuses).Disposition);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ChannelEqualToOverride_IsNotSuperseded()
    {
        // Equal is not newer. Superseding on equality would discard the override for no gain.
        var (svc, root) = NewService();
        try
        {
            var dll = WriteFakeDll(root, "nvngx_dlssd.dll");
            svc.RecordImport("nvngx_dlssd.dll", "310.7.128.0", dll, "20318080", false);

            var statuses = svc.Evaluate(
                new Dictionary<string, string?>(),
                new Dictionary<string, string?> { ["nvngx_dlssd.dll"] = "310.7.128.0" });

            Assert.NotEqual(OverrideDisposition.Superseded, Assert.Single(statuses).Disposition);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void UnknownChannelVersion_NeverSupersedes()
    {
        // Offline or a failed release query must not be read as "the channel has something newer".
        var (svc, root) = NewService();
        try
        {
            var dll = WriteFakeDll(root, "nvngx_dlssd.dll");
            svc.RecordImport("nvngx_dlssd.dll", "310.7.128.0", dll, "20318080", false);

            foreach (var channel in new string?[] { null, "", "Unknown", "N/A" })
            {
                var statuses = svc.Evaluate(
                    new Dictionary<string, string?>(),
                    new Dictionary<string, string?> { ["nvngx_dlssd.dll"] = channel });

                Assert.NotEqual(OverrideDisposition.Superseded, Assert.Single(statuses).Disposition);
            }
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void SupersededBeatsLexicalOrdering()
    {
        // 310.10 IS newer than 310.9, though it sorts earlier as a string. This is the bug class
        // this repo has now fixed in four separate places.
        var (svc, root) = NewService();
        try
        {
            var dll = WriteFakeDll(root, "nvngx_dlssd.dll");
            svc.RecordImport("nvngx_dlssd.dll", "310.9.0.0", dll, "20318080", false);

            var statuses = svc.Evaluate(
                new Dictionary<string, string?>(),
                new Dictionary<string, string?> { ["nvngx_dlssd.dll"] = "310.10.0.0" });

            Assert.Equal(OverrideDisposition.Superseded, Assert.Single(statuses).Disposition);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void MissingSourceFile_ReportsSourceMissing_NotSilentSuccess()
    {
        var (svc, root) = NewService();
        try
        {
            var dll = WriteFakeDll(root, "nvngx_dlssd.dll");
            svc.RecordImport("nvngx_dlssd.dll", "310.7.128.0", dll, "20318080", false);
            File.Delete(dll);

            var statuses = svc.Evaluate(
                new Dictionary<string, string?>(),
                new Dictionary<string, string?> { ["nvngx_dlssd.dll"] = "310.7.0.0" });

            Assert.Equal(OverrideDisposition.SourceMissing, Assert.Single(statuses).Disposition);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void NoOverrides_EvaluatesToNothing()
    {
        var (svc, root) = NewService();
        try
        {
            Assert.Empty(svc.Evaluate(
                new Dictionary<string, string?>(),
                new Dictionary<string, string?> { ["nvngx_dlssd.dll"] = "310.8.0.0" }));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void LibraryPath_DefaultsUnderAppData_AndHonoursOverride()
    {
        var (svc, root) = NewService();
        try
        {
            var def = svc.ResolveLibraryPath();
            Assert.EndsWith(OverrideManifestService.DefaultLibraryFolderName, def);

            var m = svc.Load();
            m.LibraryPath = Path.Combine(root, "custom-lib");
            svc.Save(m);

            Assert.Equal(Path.Combine(root, "custom-lib"), svc.ResolveLibraryPath());
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void HashFile_DetectsDifferentBytes()
    {
        var (svc, root) = NewService();
        try
        {
            var a = WriteFakeDll(root, "a.dll", "one");
            var b = WriteFakeDll(root, "b.dll", "two");
            var c = WriteFakeDll(root, "c.dll", "one");

            Assert.NotEqual(svc.HashFile(a), svc.HashFile(b));
            Assert.Equal(svc.HashFile(a), svc.HashFile(c));
            Assert.Null(svc.HashFile(Path.Combine(root, "nope.dll")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void CorruptManifest_DoesNotThrow()
    {
        var (svc, root) = NewService();
        try
        {
            File.WriteAllText(Path.Combine(root, OverrideManifestService.ManifestFileName), "{not json");
            var m = svc.Load();
            Assert.NotNull(m);
            Assert.Empty(m.Overrides);
        }
        finally { Directory.Delete(root, true); }
    }
}
