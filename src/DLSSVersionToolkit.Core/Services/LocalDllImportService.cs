namespace DLSSVersionToolkit.Core.Services;

using DLSSVersionToolkit.Core.Models;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

/// <summary>
/// Result of importing a folder of loose NGX DLLs into the driver's model tree.
/// </summary>
public class LocalImportResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>Every file written, as full paths (both .bin payloads and dlss_override copies).</summary>
    public List<string> FilesWritten { get; } = new();

    /// <summary>Per-component summary: DLL name → version read from its bytes → packed folder used.</summary>
    public List<ImportedComponent> Components { get; } = new();

    /// <summary>DLLs found in the source folder that were skipped, with the reason.</summary>
    public List<string> Skipped { get; } = new();

    /// <summary>Backup folder created before any overwrite, or null when nothing was overwritten.</summary>
    public string? BackupPath { get; set; }
}

public class ImportedComponent
{
    public string DllName { get; set; } = "";
    public string ComponentDir { get; set; } = "";
    public string Version { get; set; } = "";
    public string PackedFolder { get; set; } = "";
    public int BinFilesWritten { get; set; }
}

public interface ILocalDllImportService
{
    LocalImportResult ImportFromFolder(string sourceFolder, string? ngxBasePath, bool staging, bool alsoWriteOverrideTree = true);
}

/// <summary>
/// Imports loose <c>nvngx_*.dll</c> files (e.g. a DLSS 4.5 Ray Reconstruction DLL pulled out of a
/// game build, which has no official NVIDIA release asset) into NVIDIA's NGX model tree in the form
/// the driver's loader actually reads.
///
/// WHY THIS EXISTS (v0.0.51). Everything else in this toolkit acquires DLLs from a published GitHub
/// release and writes them, un-renamed, into <c>models\dlss_override\versions\{dotted}\</c>. That
/// covers nothing when the DLL only exists inside a game's files. nvidiaDlssGlom's real run log
/// shows the driver-facing layout instead:
///
///   models\dlss\versions\20318080\files\160_E658703.bin        (DLL RENAMED to arch_appid.bin)
///   models\dlss\versions\20318080\files\160_E658700.bin        (second generic app id)
///   models\dlss_override\versions\20318080\files\160_E658700\nvngx_dlssg.dll   (real name, kept)
///
/// Two things are load-bearing and both are honored here:
///
///   1. The version folder is a PACKED integer (see <see cref="NgxModelLayout"/>), and it is derived
///      PER COMPONENT from that DLL's own FileVersionInfo — not from one global version. The log
///      proves it: dlss/dlssg/dlssd landed in 20318080 (310.7.128) while deepdvc, being genuinely
///      310.7.0, landed in 20317952. Using one version for all four would misfile deepdvc.
///   2. DLL bytes are the only version source (the standing rule in this codebase). We never trust a
///      filename, a folder name, or a sidecar config.
///
/// INFERRED, NOT NVIDIA-DOCUMENTED: the arch prefix (160/Turing) and the two generic app ids
/// (E658703 / E658700) are taken from observed tool output plus emoose/DLSSTweaks#137. They are
/// consistent across two independent sources but are not published by NVIDIA. Every write is backed
/// up first for exactly that reason.
/// </summary>
[SupportedOSPlatform("windows")]
public class LocalDllImportService : ILocalDllImportService
{
    /// <summary>
    /// Components that the glom log ALSO copies into the dlss_override tree under their real DLL
    /// name (in addition to the renamed .bin). Super Resolution is absent there — it appears only as
    /// .bin payloads — so this set is exactly what was observed, not a guess extended to all four.
    /// </summary>
    private static readonly string[] OverrideTreeDlls =
        { "nvngx_dlssg.dll", "nvngx_dlssd.dll", "nvngx_deepdvc.dll" };

    /// <summary>The app-id subfolder the override tree nests the real-named DLL under.</summary>
    private const string OverrideAppIdFolder = "160_E658700";

    public LocalImportResult ImportFromFolder(
        string sourceFolder,
        string? ngxBasePath,
        bool staging,
        bool alsoWriteOverrideTree = true)
    {
        var result = new LocalImportResult();

        if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
        {
            result.ErrorMessage = $"Source folder not found: {sourceFolder}";
            return result;
        }

        // Resolve the NGX base through the shared resolver (explicit → registry → defaults) so this
        // feature cannot drift from where the rest of the app looks.
        var ngxBase = NgxPathResolver.GetCandidatePaths(ngxBasePath)
            .FirstOrDefault(p => Directory.Exists(Path.Combine(p, "models"))
                              || Directory.Exists(p));
        if (string.IsNullOrEmpty(ngxBase))
        {
            result.ErrorMessage = "Could not locate the NVIDIA NGX directory.";
            return result;
        }

        // Only ever touch paths inside NGX. Reuses the existing boundary guard rather than a second
        // hand-rolled containment check.
        if (!OperationGuard.IsPathWithin(ngxBase, ngxBase))
        {
            result.ErrorMessage = $"Refusing to write outside NGX: {ngxBase}";
            return result;
        }

        // Find the canonical DLL set in the source folder. Anything else in there is ignored.
        var found = new List<string>();
        foreach (var dllName in UpgradeService.NgxDllNames)
        {
            var candidate = Path.Combine(sourceFolder, dllName);
            if (File.Exists(candidate))
                found.Add(candidate);
        }

        if (found.Count == 0)
        {
            result.ErrorMessage =
                $"No NGX DLLs found in {sourceFolder}. Expected one or more of: {string.Join(", ", UpgradeService.NgxDllNames)}";
            return result;
        }

        // Back up the whole models tree state we are about to modify, once, before the first write.
        var backupRoot = Path.Combine(
            Path.GetTempPath(),
            $"dlss-import-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}");

        try
        {
            foreach (var srcDll in found)
            {
                var dllName = Path.GetFileName(srcDll);

                // Gate 1: real PE file. Reuses the hardened validator (MZ + PE\0\0 at e_lfanew).
                if (!OperationGuard.VerifyDllSignature(srcDll))
                {
                    result.Skipped.Add($"{dllName}: not a valid PE image");
                    continue;
                }

                // Gate 2: the DLL must declare its own version. No filename/folder inference — that
                // is the recurring bug class this codebase has fixed six times.
                var version = DllVersionReader.ReadFileVersion(srcDll);
                if (string.IsNullOrEmpty(version))
                {
                    result.Skipped.Add($"{dllName}: no version resource in the DLL");
                    continue;
                }

                var packed = NgxModelLayout.EncodePackedFolderName(version);
                if (string.IsNullOrEmpty(packed))
                {
                    result.Skipped.Add($"{dllName}: version '{version}' cannot be encoded as an NGX packed folder");
                    continue;
                }

                if (!NgxModelLayout.ComponentDirByDll.TryGetValue(dllName, out var componentDir))
                {
                    result.Skipped.Add($"{dllName}: no NGX model directory mapping");
                    continue;
                }

                var component = new ImportedComponent
                {
                    DllName = dllName,
                    ComponentDir = componentDir,
                    Version = version,
                    PackedFolder = packed
                };

                // --- 1. The .bin payloads the NGX loader reads -------------------------------
                var filesDir = NgxModelLayout.GetComponentFilesDir(ngxBase, componentDir, packed, staging);
                if (!OperationGuard.EnsureDirectoryExists(filesDir))
                {
                    result.Skipped.Add($"{dllName}: could not create {filesDir}");
                    continue;
                }

                var srcSize = new FileInfo(srcDll).Length;

                foreach (var binName in NgxModelLayout.GetBinFileNames())
                {
                    var destBin = Path.Combine(filesDir, binName);
                    BackupIfExists(destBin, ngxBase, backupRoot, result);
                    File.Copy(srcDll, destBin, true);

                    if (!OperationGuard.VerifyFile(destBin, srcSize))
                    {
                        result.Skipped.Add($"{destBin}: post-copy verification failed");
                        continue;
                    }

                    result.FilesWritten.Add(destBin);
                    component.BinFilesWritten++;
                }

                // --- 2. The override-tree copy, real DLL name, for the components the reference
                //        tool actually writes there (dlssg / dlssd / deepdvc; never SR) ---------
                if (alsoWriteOverrideTree &&
                    OverrideTreeDlls.Contains(dllName, StringComparer.OrdinalIgnoreCase))
                {
                    var overrideDir = Path.Combine(
                        staging ? Path.Combine(ngxBase, "Staging") : ngxBase,
                        "models", "dlss_override", "versions", packed,
                        NgxModelLayout.FilesLeaf, OverrideAppIdFolder);

                    if (OperationGuard.EnsureDirectoryExists(overrideDir))
                    {
                        var destDll = Path.Combine(overrideDir, dllName);
                        BackupIfExists(destDll, ngxBase, backupRoot, result);
                        File.Copy(srcDll, destDll, true);
                        if (OperationGuard.VerifyFile(destDll, srcSize))
                            result.FilesWritten.Add(destDll);
                        else
                            result.Skipped.Add($"{destDll}: post-copy verification failed");
                    }
                    else
                    {
                        result.Skipped.Add($"{dllName}: could not create {overrideDir}");
                    }
                }

                result.Components.Add(component);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            result.ErrorMessage =
                $"Access denied writing to NGX ({ex.Message}). Run DLSS Version Toolkit as Administrator.";
            return result;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Import failed: {ex.Message}";
            Debug.WriteLine($"LocalDllImportService.ImportFromFolder: {ex}");
            return result;
        }

        if (Directory.Exists(backupRoot))
            result.BackupPath = backupRoot;

        // Success means at least one component actually landed. A run that skipped everything is a
        // failure with a reason, never a silent green — the swallowed-failure lesson from v0.0.42.
        result.Success = result.Components.Count > 0 && result.FilesWritten.Count > 0;
        if (!result.Success && string.IsNullOrEmpty(result.ErrorMessage))
            result.ErrorMessage = result.Skipped.Count > 0
                ? $"No DLLs imported. {string.Join("; ", result.Skipped)}"
                : "No DLLs imported.";

        return result;
    }

    /// <summary>
    /// Copies an existing destination file into the backup root, preserving its path under NGX, so
    /// an import over a working install is reversible. Undocumented target layout = always back up.
    /// </summary>
    private static void BackupIfExists(string destPath, string ngxBase, string backupRoot, LocalImportResult result)
    {
        try
        {
            if (!File.Exists(destPath))
                return;

            var relative = Path.GetRelativePath(ngxBase, destPath);
            var backupTarget = Path.Combine(backupRoot, relative);
            var backupDir = Path.GetDirectoryName(backupTarget);
            if (!string.IsNullOrEmpty(backupDir))
                Directory.CreateDirectory(backupDir);

            File.Copy(destPath, backupTarget, true);
        }
        catch (Exception ex)
        {
            // A failed backup must not be silent — it changes the risk of the write that follows.
            result.Skipped.Add($"backup of {destPath} failed: {ex.Message}");
            Debug.WriteLine($"LocalDllImportService.BackupIfExists({destPath}): {ex.Message}");
        }
    }
}
