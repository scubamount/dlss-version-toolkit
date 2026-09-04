namespace DLSSVersionToolkit.Core.Services;

using System.IO;
using System.Runtime.Versioning;

/// <summary>
/// One component's state on disk after an update run finished writing.
/// </summary>
public class AppliedComponent
{
    public string DllName { get; set; } = "";

    /// <summary>The version actually read from the file after all writes, or a status code.</summary>
    public string Version { get; set; } = NgxConfigParser.VersionAbsent;

    /// <summary>Full path inspected. Empty when the component is absent.</summary>
    public string Path { get; set; } = "";

    public bool IsPresent => DllVersionReader.IsReportedVersion(Version);
}

/// <summary>
/// Reads what is ACTUALLY on disk in the override target after an update run has finished
/// writing, so the completion dialog can report applied state instead of intent.
///
/// Why this exists (v0.69): the Update All completion dialog was assembled from values captured
/// BEFORE the writes. Its override lines came from the pre-run manifest disposition and its
/// component lines from the channel version the run set out to install. A run could therefore
/// print "AnWave: v310.7.0.0 applied (4 files)" directly beneath
/// "Override nvngx_dlss.dll v310.7.128.0 still applied" — two different answers to "what is
/// installed right now", neither of them read from the files just written.
///
/// The rule this restores is the repo's oldest one: DLL bytes are the only version authority.
/// A report of what an operation did must be derived from the operation's effect, not its input.
/// </summary>
[SupportedOSPlatform("windows")]
public static class AppliedVersionVerifier
{
    /// <summary>
    /// The five NGX components, in the order the UI presents them.
    /// Sourced from <see cref="NgxModelLayout.ComponentDirByDll"/> so a sixth component added
    /// there is verified here automatically — a hand-copied list is a list that silently stops
    /// covering what it was written for.
    /// </summary>
    public static IEnumerable<string> ComponentDlls => NgxModelLayout.ComponentDirByDll.Keys;

    /// <summary>
    /// Inspects every NGX component installed under the override tree and returns its real
    /// post-write version. Never throws: a locked or unreadable file yields a status code, which
    /// is itself reportable information, rather than failing the run that already succeeded.
    /// </summary>
    /// <param name="ngxBasePath">NGX base, e.g. %ProgramData%\NVIDIA\NGX.</param>
    public static IReadOnlyList<AppliedComponent> Verify(string? ngxBasePath)
    {
        var results = new List<AppliedComponent>();
        if (string.IsNullOrEmpty(ngxBasePath))
            return results;

        // The override tree is versioned — models\dlss_override\versions\{packed}\... — so
        // "what is applied" means the newest version folder, ordered by the ONE ordering
        // predicate (NgxScanner.OrderVersionFoldersNewestFirst) rather than by directory
        // enumeration order, which sorts "20318080" and "310.7.0.0" lexically and lies.
        var versionsRoot = System.IO.Path.Combine(ngxBasePath, NgxScanner.ReleaseSubPath);
        if (!Directory.Exists(versionsRoot))
            return results;

        string? newest;
        try
        {
            newest = NgxScanner.OrderVersionFoldersNewestFirst(
                    Directory.GetDirectories(versionsRoot)
                        .Where(d => NgxScanner.IsVersionFolderName(System.IO.Path.GetFileName(d))))
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"AppliedVersionVerifier: cannot enumerate {versionsRoot}: {ex.Message}");
            return results;
        }

        if (newest == null)
            return results;

        foreach (var dll in ComponentDlls)
        {
            var component = new AppliedComponent { DllName = dll };
            try
            {
                // The real-named DLL sits either directly in the version folder or nested under
                // the app-id folder the override tree uses. ReadComponentVersion already handles
                // both (direct child, then shallow recursive) — same resolution the grid uses, so
                // the dialog and the grid cannot disagree about where a component lives.
                var version = DllVersionReader.ReadComponentVersion(newest, dll);

                if (version == null)
                {
                    component.Version = NgxConfigParser.VersionAbsent;
                }
                else
                {
                    component.Path = newest;
                    component.Version = DllVersionReader.IsValidVersion(version)
                        ? version
                        : NgxConfigParser.VersionUnreadable;
                }
            }
            catch (Exception ex)
            {
                // A game holding the DLL open is the common case. That is not an elevation
                // problem inside a user-owned NGX root, and it must be reported rather than
                // silently replaced with the version we intended to write.
                System.Diagnostics.Debug.WriteLine(
                    $"AppliedVersionVerifier: {dll} could not be verified: {ex.Message}");
                component.Version = NgxConfigParser.VersionUnreadable;
            }
            results.Add(component);
        }

        return results;
    }

    /// <summary>
    /// True when the applied state disagrees with what the run intended to install. The caller
    /// reports the disagreement instead of printing the intent — a partial success that prints
    /// as a clean success is the failure mode this whole change exists to remove.
    /// </summary>
    public static bool Disagrees(IReadOnlyList<AppliedComponent> applied, string dllName, string? intendedVersion)
    {
        if (!DllVersionReader.IsReportedVersion(intendedVersion))
            return false;

        var component = applied.FirstOrDefault(
            c => string.Equals(c.DllName, dllName, StringComparison.OrdinalIgnoreCase));

        // Absent is not a disagreement: not every run installs every component.
        if (component == null || !component.IsPresent)
            return false;

        return !string.Equals(component.Version, intendedVersion, StringComparison.OrdinalIgnoreCase);
    }
}
