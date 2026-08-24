namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;

/// <summary>
/// Single source of truth for "where can NGX live on this machine?" (v0.0.38).
///
/// Before this class, three call sites (ScanService.ScanAllAsync, UpgradeService
/// .GetNgxCandidatePaths, AnWaveAutoService.GetNgxCandidatePaths) each duplicated the same
/// two hardcoded probes — %ProgramData%\NVIDIA\NGX and %AppData%\NVIDIA\NGX — and missed
/// installs where the driver relocated NGX (registry-configured path). This resolver adds:
///
///   1. The explicitly configured path (settings), always first.
///   2. The driver's registry-declared NGX path:
///      HKLM\SOFTWARE\NVIDIA Corporation\Global\NGXCore : NGXPath  (newer drivers)
///      HKLM\SOFTWARE\NVIDIA Corporation\Global\NGX     : NGXResourcesPath / InstallPath
///      Read via Microsoft.Win32.Registry (64-bit view). Missing keys are normal on
///      machines without an NVIDIA driver — silently skipped.
///   3. The two default filesystem locations (unchanged behavior).
///
/// Order matters: explicit > registry > defaults, de-duplicated case-insensitively. All
/// probes are read-only. Non-Windows (CI test host) simply returns explicit + defaults.
/// </summary>
public static class NgxPathResolver
{
    /// <summary>
    /// The ONLY directories this app may ever write NGX models into: %ProgramData%\NVIDIA\NGX
    /// and %AppData%\NVIDIA\NGX. Canonical — <see cref="UpgradeService"/> derives its allowlist
    /// from this rather than rebuilding the same two literals (they drifted once already).
    ///
    /// Deliberately EXCLUDES the registry-declared path. See <see cref="GetWritableBase"/>.
    /// </summary>
    public static IReadOnlyList<string> WriteRoots => _writeRoots;

    private static readonly string[] _writeRoots = BuildWriteRoots();

    private static string[] BuildWriteRoots()
    {
        var roots = new List<string>();
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrEmpty(programData))
            roots.Add(Path.Combine(programData, "NVIDIA", "NGX"));
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
            roots.Add(Path.Combine(appData, "NVIDIA", "NGX"));
        return roots.ToArray();
    }

    /// <summary>
    /// True when <paramref name="path"/> is at or under one of <paramref name="roots"/>
    /// (defaults to <see cref="WriteRoots"/>). The roots overload exists so tests can prove the
    /// predicate on synthetic paths — the real roots differ per OS and CI is not Windows.
    /// </summary>
    public static bool IsWritableRoot(string? path, IReadOnlyList<string>? roots = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        foreach (var root in roots ?? WriteRoots)
        {
            if (OperationGuard.IsPathWithin(path, root))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves the NGX base a WRITE should target, or null when none is usable.
    ///
    /// This is NOT <see cref="GetCandidatePaths"/> — and the difference is the v0.0.53 bug.
    /// Candidates include the driver's registry-declared NGX path, which on a real machine is
    /// <c>C:\WINDOWS\System32\DriverStore\FileRepository\nv_dispi.inf_amd64_*\</c>: the driver's
    /// own binaries, owned by TrustedInstaller, NOT a model root. Reading it is harmless (the
    /// scanner just finds no models there); writing to it fails for every DLL even as
    /// Administrator, because the DriverStore denies write to Administrators by design.
    ///
    /// So writers filter to <see cref="WriteRoots"/> first, then prefer a root that already has a
    /// models tree, and only then fall back to the first write root (first run, nothing created
    /// yet). An explicit user-configured path is honored only if it is itself a write root.
    /// </summary>
    public static string? GetWritableBase(string? explicitPath = null)
    {
        var writable = GetCandidatePaths(explicitPath)
            .Where(p => IsWritableRoot(p))
            .ToList();

        // Prefer a base that already holds a models tree (Release or Staging).
        var withModels = writable.FirstOrDefault(p =>
            Directory.Exists(Path.Combine(p, "models")) ||
            Directory.Exists(Path.Combine(p, "Staging", "models")));
        if (!string.IsNullOrEmpty(withModels))
            return withModels;

        // Then any write root that at least exists.
        var existing = writable.FirstOrDefault(Directory.Exists);
        if (!string.IsNullOrEmpty(existing))
            return existing;

        // Nothing exists yet — return the canonical first root so a first import can create it.
        return writable.FirstOrDefault() ?? WriteRoots.FirstOrDefault();
    }

    /// <summary>
    /// Returns NGX base-path candidates in priority order (explicit → registry → defaults).
    /// Never throws; never returns duplicates. Entries are NOT filtered by Directory.Exists —
    /// callers that need existing dirs filter themselves (the scanner tolerates missing paths).
    ///
    /// READ-ONLY USE. Any caller that will WRITE must use <see cref="GetWritableBase"/> instead:
    /// this list can contain the driver-store path, which is unwritable by design.
    /// </summary>
    public static List<string> GetCandidatePaths(string? explicitPath = null)
    {
        var candidates = new List<string>();

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;
            var trimmed = path.Trim();
            if (!candidates.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                candidates.Add(trimmed);
        }

        // 1. Explicitly configured path (settings or parameter) always wins.
        Add(explicitPath);

        // 2. Driver-declared NGX locations from the registry.
        foreach (var regPath in GetRegistryNgxPaths())
            Add(regPath);

        // 3. Default known filesystem paths (the pre-v0.0.38 behavior).
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        if (!string.IsNullOrEmpty(programData))
            Add(Path.Combine(programData, "NVIDIA", "NGX"));

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
            Add(Path.Combine(appData, "NVIDIA", "NGX"));

        return candidates;
    }

    /// <summary>
    /// Reads NGX path hints from the NVIDIA driver's registry keys. Returns an empty list on
    /// non-Windows, missing keys, or access failure — never throws.
    /// </summary>
    public static List<string> GetRegistryNgxPaths()
    {
        var results = new List<string>();
        if (!OperatingSystem.IsWindows())
            return results;

        // (key, value-name) pairs seen across driver generations. NGXCore\NGXPath is what
        // current drivers write; the Global\NGX values are older but harmless to probe.
        var probes = new (string Key, string Value)[]
        {
            (@"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "NGXPath"),
            (@"SOFTWARE\NVIDIA Corporation\Global\NGXCore", "FullPath"),
            (@"SOFTWARE\NVIDIA Corporation\Global\NGX", "NGXResourcesPath"),
            (@"SOFTWARE\NVIDIA Corporation\Global\NGX", "InstallPath"),
        };

        try
        {
            using var hklm = Microsoft.Win32.RegistryKey.OpenBaseKey(
                Microsoft.Win32.RegistryHive.LocalMachine,
                Microsoft.Win32.RegistryView.Registry64);

            foreach (var (keyPath, valueName) in probes)
            {
                try
                {
                    using var key = hklm.OpenSubKey(keyPath);
                    if (key?.GetValue(valueName) is string raw && !string.IsNullOrWhiteSpace(raw))
                    {
                        var expanded = Environment.ExpandEnvironmentVariables(raw.Trim());
                        // Registry may point at the NGX core dir itself or a parent — accept
                        // as-is; the scanner appends models\dlss_override\versions and simply
                        // finds nothing if the layout doesn't match.
                        if (!results.Contains(expanded, StringComparer.OrdinalIgnoreCase))
                            results.Add(expanded);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"NgxPathResolver: probe {keyPath}\\{valueName} failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"NgxPathResolver: registry unavailable: {ex.Message}");
        }

        return results;
    }
}
