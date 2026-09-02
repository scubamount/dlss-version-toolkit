namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;

/// <summary>
/// Writes and activates the Streamline-plugin override trees — the mechanism nvidiaDlssGlom uses
/// and this toolkit previously skipped (v0.67). Reverse-engineered ground truth, cited fully in
/// <see cref="NgxModelLayout"/>'s streamline section:
///
///   payload:  models\sl_&lt;plugin&gt;_0\versions\&lt;packed&gt;\files\160_E658703.dll
///   activate: nvngx_config.txt section  [sl_&lt;plugin&gt;_0] / app_E658703 = &lt;dotted version&gt;
///
/// Versions come from DLL bytes only (the standing rule): a payload already on disk is decoded
/// from its packed folder name, and a folder name that does not decode is reported, not trusted.
/// Syncing is idempotent: overwriting a payload means the SAME packed version and the SAME
/// versioned DLL — a byte-identical rewrite — so no per-payload backup is needed.
/// </summary>
public static class StreamlineOverrideService
{
    public sealed record SyncOutcome(List<string> Written, List<string> Skipped);

    /// <summary>
    /// Copies every mapped Streamline plugin DLL present in <paramref name="sourceBinPath"/>
    /// into its plugin model tree under <paramref name="ngxBasePath"/>. Absent plugins are not
    /// skipped-with-error: the map is a superset of any given SDK, exactly like the nvngx set.
    /// </summary>
    public static SyncOutcome SyncPlugins(string sourceBinPath, string ngxBasePath, bool staging = false)
    {
        var written = new List<string>();
        var skipped = new List<string>();

        foreach (var (dllName, componentDir) in NgxModelLayout.StreamlinePluginDirByDll)
        {
            var srcDll = Path.Combine(sourceBinPath, dllName);
            if (!File.Exists(srcDll)) continue; // not in this SDK — not applicable, not a failure

            if (!OperationGuard.VerifyDllSignature(srcDll))
            {
                skipped.Add($"{dllName}: PE signature check failed");
                continue;
            }

            var version = DllVersionReader.ReadFileVersion(srcDll);
            var packed = NgxModelLayout.EncodePackedFolderName(version);
            if (string.IsNullOrEmpty(packed))
            {
                skipped.Add($"{dllName}: version '{version}' cannot be encoded as an NGX packed folder");
                continue;
            }

            var filesDir = NgxModelLayout.GetComponentFilesDir(ngxBasePath, componentDir, packed!, staging);
            if (!OperationGuard.EnsureDirectoryExists(filesDir))
            {
                skipped.Add($"{dllName}: could not create {filesDir}");
                continue;
            }

            var srcSize = new FileInfo(srcDll).Length;
            foreach (var payloadName in NgxModelLayout.GetStreamlinePayloadFileNames())
            {
                var dest = Path.Combine(filesDir, payloadName);
                try
                {
                    File.Copy(srcDll, dest, true);
                }
                catch (Exception ex)
                {
                    skipped.Add($"{dllName}: copy to {payloadName} failed: {ex.Message}");
                    continue;
                }

                if (!OperationGuard.VerifyFile(dest, srcSize))
                {
                    try { File.Delete(dest); } catch { }
                    skipped.Add($"{dllName}: post-copy verification failed for {payloadName}");
                    continue;
                }

                written.Add(Path.Combine(componentDir, "versions", packed!, "files", payloadName));
            }
        }

        return new SyncOutcome(written, skipped);
    }

    /// <summary>One activated plugin: model dir + version decoded from the on-disk packed folder.</summary>
    public sealed record InstalledPlugin(string ComponentDir, string Version);

    /// <summary>
    /// Which plugins are activated right now: every plugin model tree with at least one payload,
    /// newest packed folder per plugin. Version authority is the folder DECODE (bytes-derived at
    /// write time) — never nvngx_config.txt, which is state, not evidence.
    /// </summary>
    public static List<InstalledPlugin> InstalledPlugins(string ngxBasePath, bool staging = false)
    {
        var result = new List<InstalledPlugin>();
        var modelsRoot = staging
            ? Path.Combine(ngxBasePath, "Staging", "models")
            : Path.Combine(ngxBasePath, "models");
        if (!Directory.Exists(modelsRoot)) return result;

        try
        {
            foreach (var dir in NgxModelLayout.StreamlinePluginDirByDll.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var pluginRoot = Path.Combine(modelsRoot, dir);
                if (!Directory.Exists(pluginRoot)) continue;

                // Newest first, mirroring how the loader resolves multiple versions.
                var versionsRoot = Path.Combine(pluginRoot, "versions");
                var packed = Directory.Exists(versionsRoot)
                    ? Directory.GetDirectories(versionsRoot)
                        .Select(d => new DirectoryInfo(d).Name)
                        .Where(NgxModelLayout.IsPackedVersionFolderName)
                        .Select(n => (name: n, v: NgxModelLayout.DecodePackedVersion(n)))
                        .Where(t => t.v != null)
                        .OrderByDescending(t => t.v)
                        .FirstOrDefault()
                    : default;

                if (packed.name == null) continue;

                var filesDir = NgxModelLayout.GetComponentFilesDir(ngxBasePath, dir, packed.name, staging);
                var hasPayload = NgxModelLayout.GetStreamlinePayloadFileNames()
                    .Any(p => File.Exists(Path.Combine(filesDir, p)));
                if (hasPayload && packed.v != null)
                    result.Add(new InstalledPlugin(dir, packed.v!.ToString(3)));
            }
        }
        catch (Exception ex) { Debug.WriteLine($"InstalledPlugins: error: {ex.Message}"); }

        return result;
    }
}
