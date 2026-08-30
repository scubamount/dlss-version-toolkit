namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;

/// <summary>
/// Reads the real version of an NGX DLL from its PE FileVersionInfo. This is the SINGLE SOURCE
/// OF TRUTH for "what version is this DLL", replacing the old approach that parsed a
/// <c>nvngx_package_config.txt</c> from inside the SDK zip — a file that the current NVIDIA/DLSS
/// demo zip and the NVIDIA-RTX/Streamline SDK zip DO NOT ship (verified against v310.7.0 /
/// v2.12.0 artifacts). When the config is absent the old path fell back to a HARDCODED "310.6.0",
/// which is exactly why AnWave wrote a stale 310.6 override after a 310.7 DLL copy.
///
/// FileVersionInfo.GetVersionInfo reads the VS_FIXEDFILEINFO/StringFileInfo block; nvngx_dlss.dll
/// 310.7.0.0 reports FileVersion "310.7.0.0". This is Windows-only (the version resource API is a
/// Win32 call) but the whole toolkit is win-x64 only.
/// </summary>
[SupportedOSPlatform("windows")]
public static class DllVersionReader
{
    private const string PrimaryDll = "nvngx_dlss.dll";

    /// <summary>
    /// "Is this string a plausible version at all?" — the ONE definition. Three scanners
    /// (GlobalScanner, StreamlineScanner, NgxConfigParser) each carried a byte-identical private
    /// copy of this regex until v0.0.61; any future divergence means two surfaces disagree on
    /// whether the same DLL version is valid. A gate scans the tree for new private copies.
    /// Accepts 2- to 4-part dotted versions ("310.6", "310.7.0.0"); rejects garbage and blanks.
    /// </summary>
    // 2-4 dotted parts, digit groups only. {0,2} — the original copies' {1,3} was an
    // off-by-one: it rejected 2-part versions ("310.6" from config files/BuildIDs) and
    // accepted 5-part garbage. Comparers pad short forms to 4 downstream. (v0.0.62)
    public static bool IsValidVersion(string? version) =>
        !string.IsNullOrEmpty(version) &&
        System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+(\.\d+){0,2}$");

    /// <summary>
    /// Reads the FileVersion of a single DLL (e.g. "310.7.0.0"). Returns null if the file is
    /// missing or carries no version resource. Prefers FileVersion, falls back to ProductVersion.
    /// </summary>
    public static string? ReadFileVersion(string dllPath)
    {
        try
        {
            if (string.IsNullOrEmpty(dllPath) || !File.Exists(dllPath))
                return null;

            var info = FileVersionInfo.GetVersionInfo(dllPath);

            // FileVersion is the dotted string ("310.7.0.0"). Some DLLs only populate the numeric
            // FileVersionPart fields, so rebuild from those when the string is empty.
            var fileVersion = info.FileVersion?.Trim();
            if (!string.IsNullOrEmpty(fileVersion))
                return NormalizeCommaVersion(fileVersion);

            if (info.FileMajorPart != 0 || info.FileMinorPart != 0 ||
                info.FileBuildPart != 0 || info.FilePrivatePart != 0)
                return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";

            var productVersion = info.ProductVersion?.Trim();
            return string.IsNullOrEmpty(productVersion) ? null : NormalizeCommaVersion(productVersion);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DllVersionReader.ReadFileVersion failed for {dllPath}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Reads the DLSS version for a folder by locating <c>nvngx_dlss.dll</c> directly inside it
    /// (non-recursive, so a sibling version folder can never bleed in) and reading its
    /// FileVersionInfo. Returns null when the DLL is absent.
    /// </summary>
    public static string? ReadDlssVersionFromFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return null;
        var dll = Path.Combine(folder, PrimaryDll);
        return ReadFileVersion(dll);
    }

    /// <summary>
    /// Reads the version of a specific NGX component DLL inside a version folder, searching this
    /// folder and its subfolders (NGX layouts sometimes nest the DLL one level deep). Used by the
    /// scanner to report the REAL on-disk version from the DLL bytes — the authoritative source —
    /// rather than a sidecar nvngx_package_config.txt that current SDK zips don't ship and that
    /// goes stale when DLLs are swapped into an existing (differently-named) version folder.
    /// </summary>
    /// <param name="folder">The NGX version folder.</param>
    /// <param name="dllName">e.g. "nvngx_dlss.dll", "nvngx_dlssg.dll".</param>
    public static string? ReadComponentVersion(string folder, string dllName)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return null;
        try
        {
            // Direct child first (the common case), then a shallow recursive search.
            var direct = Path.Combine(folder, dllName);
            if (File.Exists(direct))
                return ReadFileVersion(direct);

            var found = Directory.GetFiles(folder, dllName, SearchOption.AllDirectories).FirstOrDefault();
            return found != null ? ReadFileVersion(found) : null;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"DllVersionReader.ReadComponentVersion failed for {folder}/{dllName}: {ex.Message}");
            return null;
        }
    }

    // Some version resources use comma separators ("310,7,0,0"); normalize to dots.
    private static string NormalizeCommaVersion(string v) => v.Replace(',', '.').Trim();
}
