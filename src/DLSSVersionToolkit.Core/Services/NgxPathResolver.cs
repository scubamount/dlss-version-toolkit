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
    /// Returns NGX base-path candidates in priority order (explicit → registry → defaults).
    /// Never throws; never returns duplicates. Entries are NOT filtered by Directory.Exists —
    /// callers that need existing dirs filter themselves (the scanner tolerates missing paths).
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
