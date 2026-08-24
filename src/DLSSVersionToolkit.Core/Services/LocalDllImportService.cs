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

    /// <summary>
    /// Per-component summary: DLL name → version read from its bytes → packed folder used.
    /// Holds ONLY components that actually landed a file. A component whose every write failed is
    /// in <see cref="Skipped"/> with its reason and is deliberately absent here, so
    /// <c>Components.Count</c> means exactly "components imported" and cannot overstate the run.
    /// </summary>
    public List<ImportedComponent> Components { get; } = new();

    /// <summary>DLLs found in the source folder that were skipped, with the reason.</summary>
    public List<string> Skipped { get; } = new();

    /// <summary>Backup folder created before any overwrite, or null when nothing was overwritten.</summary>
    public string? BackupPath { get; set; }

    /// <summary>
    /// THE predicate for "this import put files on disk". Because <see cref="Components"/> only
    /// holds landed components, this is provably equivalent to <c>FilesWritten.Count &gt; 0</c> —
    /// the point is that there is now ONE of it. Callers previously each rebuilt the test as
    /// <c>Success &amp;&amp; FilesWritten.Count &gt; 0</c>, which is two sites that can disagree.
    /// </summary>
    public bool Landed => Components.Count > 0;

    /// <summary>
    /// Total files written across every component, summed from the per-component numbers that get
    /// shown to the user. Reconciling this against <see cref="FilesWritten"/> is what catches a
    /// report that displays a headline total its own breakdown does not add up to.
    /// </summary>
    public int TotalFilesWrittenFromComponents => Components.Sum(c => c.TotalFilesWritten);
}

public class ImportedComponent
{
    public string DllName { get; set; } = "";
    public string ComponentDir { get; set; } = "";
    public string Version { get; set; } = "";
    public string PackedFolder { get; set; } = "";

    /// <summary>Renamed <c>arch_appid.bin</c> payloads written under <c>models\{component}\</c>.</summary>
    public int BinFilesWritten { get; set; }

    /// <summary>
    /// Real-named DLL copies written under <c>models\dlss_override\</c>. Only dlssg/dlssd/deepdvc
    /// ever get one. Counted separately because it was previously counted in the run total and
    /// named nowhere in the breakdown — 4 components × 2 bins reads as 8 next to a headline of 11.
    /// </summary>
    public int OverrideFilesWritten { get; set; }

    /// <summary>Every file this component landed, in either tree.</summary>
    public int TotalFilesWritten => BinFilesWritten + OverrideFilesWritten;

    /// <summary>
    /// True when this component put at least one file on disk, in EITHER tree. The manifest gate
    /// used to ask <c>BinFilesWritten &gt; 0</c>, which is false for a component whose bins failed
    /// verification but whose override copy succeeded — a written file with no record of the
    /// override, which is precisely what the manifest exists to prevent.
    /// </summary>
    public bool Landed => TotalFilesWritten > 0;
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
    /// Records each successful import so Update All can preserve it. Optional: when null the
    /// service still imports, it just keeps no memory of having done so (used by tests that only
    /// exercise the file layout).
    /// </summary>
    private readonly IOverrideManifestService? _manifest;

    public LocalDllImportService(IOverrideManifestService? manifest = null)
    {
        _manifest = manifest;
    }

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

        // Resolve the NGX base a WRITE may target. NOT GetCandidatePaths: that list is led by the
        // driver's registry-declared path, which is the DriverStore
        // (C:\WINDOWS\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_*) — TrustedInstaller
        // territory, unwritable even elevated. v0.0.52 picked it and every DLL failed to import.
        var ngxBase = NgxPathResolver.GetWritableBase(ngxBasePath);
        if (string.IsNullOrEmpty(ngxBase))
        {
            result.ErrorMessage =
                "Could not locate a writable NVIDIA NGX directory (expected " +
                string.Join(" or ", NgxPathResolver.WriteRoots) + ").";
            return result;
        }

        // Belt and braces: prove the resolved base is inside an allowed write root before creating
        // anything. The previous form compared ngxBase to ITSELF, so it was always true and could
        // never have caught the driver-store path it was meant to stop.
        if (!NgxPathResolver.IsWritableRoot(ngxBase))
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
                        {
                            result.FilesWritten.Add(destDll);
                            component.OverrideFilesWritten++;
                        }
                        else
                        {
                            result.Skipped.Add($"{destDll}: post-copy verification failed");
                        }
                    }
                    else
                    {
                        result.Skipped.Add($"{dllName}: could not create {overrideDir}");
                    }
                }

                // A component that landed nothing is NOT an import. It stays out of Components (so
                // the reported count cannot overstate the run) and its reason is already in Skipped.
                if (!component.Landed)
                {
                    result.Skipped.Add($"{dllName}: no files written, not imported");
                    continue;
                }

                result.Components.Add(component);

                // Record the assertion. This is what makes the import survive a later Update All:
                // without a record, the next sync overwrites it and nothing knows it ever happened.
                // Gated on Landed (files in EITHER tree), not on bins alone — a component whose
                // bins failed verification but whose override copy succeeded has a file on disk,
                // and skipping the record there is the exact silent-overwrite this manifest exists
                // to stop.
                try
                {
                    _manifest?.RecordImport(dllName, version, srcDll, packed, staging);
                }
                catch (Exception ex)
                {
                    // Manifest trouble must not fail a successful copy, but it MUST be visible —
                    // a silent miss here is what re-breaks Update All preservation.
                    result.Skipped.Add($"{dllName}: imported, but recording the override failed ({ex.Message})");
                }
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // NOT an elevation prompt. Every write here is inside an allowlisted write root
            // (ProgramData / AppData NVIDIA\NGX), which a normal user owns — v0.0.53 proved the
            // v0.0.52 "run as Administrator" advice was chasing a path problem. What genuinely
            // denies a write in these roots is a file held open by a running game or a read-only
            // attribute, so say that instead of sending the user to elevate for no reason.
            result.ErrorMessage =
                $"Access denied writing to {ngxBase} ({ex.Message}). Close any running game — " +
                "it can hold an NGX DLL open — then try again.";
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
        // One predicate (LocalImportResult.Landed) so callers cannot ask a different question.
        result.Success = result.Landed;
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
