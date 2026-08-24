using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text.Json;
using DLSSVersionToolkit.Core.Models;

namespace DLSSVersionToolkit.Core.Services;

/// <summary>What Update All should do about one override, decided before anything is written.</summary>
public enum OverrideDisposition
{
    /// <summary>Override bytes are still in place and still the newest thing available. Nothing to do.</summary>
    Intact,

    /// <summary>Override was overwritten (or never landed) and the channel has nothing newer — re-assert it.</summary>
    NeedsReassert,

    /// <summary>The download channel now offers a version NEWER than the override. Leave the channel
    /// DLL alone and tell the user their manual import has been overtaken.</summary>
    Superseded,

    /// <summary>The library file backing this override is gone, so it cannot be re-asserted.</summary>
    SourceMissing
}

public class OverrideStatus
{
    public string DllName { get; set; } = "";
    public string OverrideVersion { get; set; } = "";

    /// <summary>Version currently sitting in NGX for this component, if any.</summary>
    public string? InstalledVersion { get; set; }

    /// <summary>Newest version the download channel could supply, if known.</summary>
    public string? ChannelVersion { get; set; }

    public OverrideDisposition Disposition { get; set; }

    /// <summary>True when NGX bytes still hash to the imported DLL.</summary>
    public bool BytesMatch { get; set; }

    public string Explanation { get; set; } = "";
}

public interface IOverrideManifestService
{
    OverrideManifest Load();
    void Save(OverrideManifest manifest);

    /// <summary>Absolute path of the drop folder — the configured one, or the app default.</summary>
    string ResolveLibraryPath(OverrideManifest? manifest = null);

    /// <summary>Records an import. Replaces any prior record for the same DLL.</summary>
    void RecordImport(string dllName, string version, string sourcePath, string packedFolder, bool staging);

    void Remove(string dllName);

    /// <summary>Version of an override for one component, or null when not overridden.</summary>
    string? GetOverrideVersion(string dllName);

    /// <summary>
    /// Decides what should happen to every recorded override, given what is installed now and what
    /// the download channel can currently offer.
    /// </summary>
    List<OverrideStatus> Evaluate(
        IReadOnlyDictionary<string, string?> installedByDll,
        IReadOnlyDictionary<string, string?> channelByDll);

    /// <summary>SHA-256 of a file, or null if unreadable.</summary>
    string? HashFile(string path);
}

/// <summary>
/// Persists and reasons about locally-imported DLL overrides (v0.0.52).
///
/// THE PROBLEM THIS SOLVES. A local import asserts "this DLL beats whatever you would download."
/// Nothing recorded that assertion, so the next Update All happily overwrote it and reported
/// success. But blindly re-asserting is equally wrong: the day NVIDIA publishes something newer
/// than the manually-imported build, re-asserting is a silent DOWNGRADE. So the decision is made by
/// comparing versions, never by assuming the override always wins.
///
/// Comparison goes through <see cref="IVersionComparer"/> — the same predicate the rest of the app
/// uses to decide "is this newer". A second, private comparison here is exactly how this codebase
/// grew its lexical-vs-numeric sort bugs.
/// </summary>
[SupportedOSPlatform("windows")]
public class OverrideManifestService : IOverrideManifestService
{
    private readonly IVersionComparer _comparer;
    private readonly string _manifestPath;
    private readonly string _defaultLibraryPath;

    public const string ManifestFileName = "overrides.json";
    public const string DefaultLibraryFolderName = "Overrides";

    public OverrideManifestService(IVersionComparer comparer, string? appDataRoot = null)
    {
        _comparer = comparer;

        var root = appDataRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DLSSVersionToolkit");

        _manifestPath = Path.Combine(root, ManifestFileName);
        _defaultLibraryPath = Path.Combine(root, DefaultLibraryFolderName);
    }

    public OverrideManifest Load()
    {
        try
        {
            if (!File.Exists(_manifestPath))
                return new OverrideManifest();

            var json = File.ReadAllText(_manifestPath);
            return JsonSerializer.Deserialize<OverrideManifest>(json) ?? new OverrideManifest();
        }
        catch (Exception ex)
        {
            // A corrupt manifest must not brick the app; an empty one just means "no overrides".
            System.Diagnostics.Debug.WriteLine($"OverrideManifest load failed: {ex.Message}");
            return new OverrideManifest();
        }
    }

    public void Save(OverrideManifest manifest)
    {
        var dir = Path.GetDirectoryName(_manifestPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_manifestPath, json);
    }

    public string ResolveLibraryPath(OverrideManifest? manifest = null)
    {
        var m = manifest ?? Load();
        return string.IsNullOrWhiteSpace(m.LibraryPath) ? _defaultLibraryPath : m.LibraryPath;
    }

    public void RecordImport(string dllName, string version, string sourcePath, string packedFolder, bool staging)
    {
        var manifest = Load();
        manifest.Overrides.RemoveAll(o =>
            string.Equals(o.DllName, dllName, StringComparison.OrdinalIgnoreCase));

        manifest.Overrides.Add(new OverrideRecord
        {
            DllName = dllName,
            Version = version,
            Sha256 = HashFile(sourcePath) ?? "",
            SourcePath = sourcePath,
            PackedFolder = packedFolder,
            Staging = staging,
            ImportedAt = DateTime.UtcNow
        });

        Save(manifest);
    }

    public void Remove(string dllName)
    {
        var manifest = Load();
        var removed = manifest.Overrides.RemoveAll(o =>
            string.Equals(o.DllName, dllName, StringComparison.OrdinalIgnoreCase));
        if (removed > 0)
            Save(manifest);
    }

    public string? GetOverrideVersion(string dllName)
    {
        var rec = Load().Overrides.FirstOrDefault(o =>
            string.Equals(o.DllName, dllName, StringComparison.OrdinalIgnoreCase));
        return rec?.Version;
    }

    public List<OverrideStatus> Evaluate(
        IReadOnlyDictionary<string, string?> installedByDll,
        IReadOnlyDictionary<string, string?> channelByDll)
    {
        var results = new List<OverrideStatus>();

        foreach (var rec in Load().Overrides)
        {
            installedByDll.TryGetValue(rec.DllName, out var installed);
            channelByDll.TryGetValue(rec.DllName, out var channel);

            var status = new OverrideStatus
            {
                DllName = rec.DllName,
                OverrideVersion = rec.Version,
                InstalledVersion = installed,
                ChannelVersion = channel
            };

            // The channel winning is checked FIRST and independently of whether the override bytes
            // are still in place. A superseded override must never be re-asserted even when it has
            // been overwritten — that overwrite was an upgrade, not damage.
            if (!string.IsNullOrWhiteSpace(channel) &&
                !string.IsNullOrWhiteSpace(rec.Version) &&
                _comparer.IsNewer(channel!, rec.Version))
            {
                status.Disposition = OverrideDisposition.Superseded;
                status.Explanation =
                    $"Published {channel} is newer than your imported {rec.Version} — " +
                    "the download will be kept and your override left unapplied.";
                results.Add(status);
                continue;
            }

            // Verify by BYTES, not by the manifest's own say-so. The record is a claim; the hash is
            // the evidence. Anything editing NGX outside this app shows up here.
            var installedHashMatches = false;
            if (!string.IsNullOrWhiteSpace(rec.Sha256))
            {
                var ngxCopy = FindInstalledCopy(rec);
                if (ngxCopy != null)
                {
                    var hash = HashFile(ngxCopy);
                    installedHashMatches = string.Equals(hash, rec.Sha256, StringComparison.OrdinalIgnoreCase);
                }
            }
            status.BytesMatch = installedHashMatches;

            if (installedHashMatches)
            {
                status.Disposition = OverrideDisposition.Intact;
                status.Explanation = $"Override {rec.Version} still in place.";
            }
            else if (!File.Exists(rec.SourcePath))
            {
                status.Disposition = OverrideDisposition.SourceMissing;
                status.Explanation =
                    $"Cannot re-apply {rec.Version}: the imported file is gone from {rec.SourcePath}.";
            }
            else
            {
                status.Disposition = OverrideDisposition.NeedsReassert;
                status.Explanation = $"Override {rec.Version} will be re-applied after the update.";
            }

            results.Add(status);
        }

        return results;
    }

    /// <summary>
    /// Locates the imported .bin inside NGX for a record, so its bytes can be hashed. Returns null
    /// when the expected path does not exist.
    /// </summary>
    private static string? FindInstalledCopy(OverrideRecord rec)
    {
        try
        {
            if (!NgxModelLayout.ComponentDirByDll.TryGetValue(rec.DllName, out var componentDir))
                return null;

            // Must match where the import actually wrote — same writable-base rule, not the raw
            // candidate list (whose first entry is the unwritable driver store).
            var ngxBase = NgxPathResolver.GetWritableBase(null);
            if (string.IsNullOrEmpty(ngxBase))
                return null;

            var filesDir = NgxModelLayout.GetComponentFilesDir(
                ngxBase, componentDir, rec.PackedFolder, rec.Staging);

            if (!Directory.Exists(filesDir))
                return null;

            return Directory.GetFiles(filesDir, "*.bin").FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"FindInstalledCopy failed for {rec.DllName}: {ex.Message}");
            return null;
        }
    }

    public string? HashFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            using var sha = SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"HashFile failed for {path}: {ex.Message}");
            return null;
        }
    }
}
