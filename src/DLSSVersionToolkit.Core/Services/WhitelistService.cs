using System.ComponentModel;
namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;
using System.Text.Json;

public sealed record WhitelistResult(
    bool Success,
    int GamesModified,
    string? ErrorMessage,
    List<string> ModifiedFiles,
    bool IsApplicable = true);

/// <summary>
/// Current whitelist state on disk, derived by reading (never mutating) NVIDIA App's
/// ApplicationStorage.json and fingerprint.db files.
/// </summary>
public enum WhitelistState
{
    /// <summary>NVIDIA App backend files were not found — whitelisting does not apply here.</summary>
    NotApplicable,
    /// <summary>One or more Disable_*_Override flags are still ON (true / "1") — not yet whitelisted.</summary>
    NotApplied,
    /// <summary>All Disable_*_Override flags are OFF (false / "0") — whitelist already in effect.</summary>
    Applied
}

public interface IWhitelistService
{
    Task<WhitelistResult> ApplyWhitelistAsync(CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> RestartNvidiaServicesAsync(CancellationToken ct = default);

    /// <summary>
    /// Flips <c>"IsOpsSupported":false</c> to <c>true</c> for NVIDIA-identified apps, which
    /// unlocks NVIDIA App's DLSS Override UI for titles it labels "not supported".
    /// Separate from <see cref="ApplyWhitelistAsync"/> on purpose — see
    /// <see cref="WhitelistService.UnlockUnsupportedGamesAsync"/> for the risk rationale.
    /// </summary>
    Task<WhitelistResult> UnlockUnsupportedGamesAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads the current whitelist state from disk without changing anything. Used at startup /
    /// scan time so the UI reflects reality (e.g. a whitelist a previous run already applied)
    /// instead of always showing "Not applied" until the user clicks Apply this session.
    /// </summary>
    Task<WhitelistState> DetectStateAsync(CancellationToken ct = default);
}

public sealed class WhitelistService : IWhitelistService
{
    private static readonly string NvBackendBasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NVIDIA Corporation", "NVIDIA app", "NvBackend");

    private static readonly string ApplicationStoragePath = Path.Combine(NvBackendBasePath, "ApplicationStorage.json");

    // Explicit allowlist, NOT a blanket match on keys ending in "_Override". A comparable tool
    // (horovodovodo4ka/dlss-overrides-enabler) does `replace("_Override\":true", ...)` across the
    // whole file, which silently flips any future key NVIDIA adds. Keep this list explicit.
    // This is the complete verified set (cross-checked against JPersson77's reference script and
    // kaanaldemir/DLSS-Override-For-All-Games). Disable_MFG_Override / Disable_DFG_Override do
    // NOT exist — no tool and no NVIDIA source references them.
    private static readonly string[] DisableOverrideKeys =
    {
        "Disable_FG_Override",
        "Disable_RR_Override",
        "Disable_SR_Override",
        "Disable_RR_Model_Override",
        "Disable_SR_Model_Override"
    };

public async Task<WhitelistResult> ApplyWhitelistAsync(CancellationToken ct = default)
{
    var modifiedFiles = new List<string>();
    int gamesModified = 0;
    string? errorMessage = null;
    bool isApplicable = true;

    // Step 1: Modify ApplicationStorage.json
    try
    {
        var jsonResult = await ModifyApplicationStorageJsonAsync(ct);
        if (!jsonResult.Success && !string.IsNullOrEmpty(jsonResult.ErrorMessage))
        {
            errorMessage = jsonResult.ErrorMessage;
        }
        gamesModified += jsonResult.GamesModified;
        modifiedFiles.AddRange(jsonResult.ModifiedFiles);
        isApplicable = jsonResult.IsApplicable; // Not applicable if file missing
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"ApplyWhitelistAsync: ApplicationStorage.json error: {ex.Message}");
        errorMessage = $"Failed to process ApplicationStorage.json: {ex.Message}";
    }

    // Step 2: Modify fingerprint.db files
    try
    {
        var xmlResult = ModifyFingerprintDatabases();
        if (!xmlResult.Success && !string.IsNullOrEmpty(xmlResult.ErrorMessage))
        {
            if (!string.IsNullOrEmpty(errorMessage))
                errorMessage += "; " + xmlResult.ErrorMessage;
            else
                errorMessage = xmlResult.ErrorMessage;
        }
        gamesModified += xmlResult.GamesModified;
        modifiedFiles.AddRange(xmlResult.ModifiedFiles);
        // If fingerprint.dbs were found, the NVIDIA app is installed even if ApplicationStorage.json is missing
        if (xmlResult.IsApplicable)
            isApplicable = true;
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"ApplyWhitelistAsync: fingerprint.db error: {ex.Message}");
        if (!string.IsNullOrEmpty(errorMessage))
            errorMessage += "; " + ex.Message;
        else
            errorMessage = $"Failed to process fingerprint.db: {ex.Message}";
    }

    return new WhitelistResult(
        Success: string.IsNullOrEmpty(errorMessage),
        GamesModified: gamesModified,
        ErrorMessage: string.IsNullOrEmpty(errorMessage) ? null : errorMessage,
        ModifiedFiles: modifiedFiles,
        IsApplicable: isApplicable);
    }

    /// <summary>
    /// Reads ApplicationStorage.json + every fingerprint.db and reports whether the whitelist
    /// is already in effect, WITHOUT writing anything. The applied/not-applied decision uses the
    /// exact same flag definitions as <see cref="ApplyWhitelistAsync"/> so the two cannot drift:
    /// any Disable_*_Override still ON anywhere ⇒ NotApplied; none found anywhere ⇒ NotApplicable;
    /// flags present and all OFF ⇒ Applied.
    /// </summary>
    public async Task<WhitelistState> DetectStateAsync(CancellationToken ct = default)
    {
        bool anyBackendFilePresent = false;
        bool anyFlagStillOn = false;

        // ApplicationStorage.json — count "Disable_*_Override": true occurrences (read-only).
        if (File.Exists(ApplicationStoragePath))
        {
            anyBackendFilePresent = true;
            try
            {
                var json = await File.ReadAllTextAsync(ApplicationStoragePath, ct);
                if (!string.IsNullOrWhiteSpace(json) && CountTrueJsonDisableFlags(json) > 0)
                    anyFlagStillOn = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DetectStateAsync: could not read ApplicationStorage.json: {ex.Message}");
            }
        }

        // fingerprint.db files — any <Disable_*_Override>1</...> means still-on (read-only).
        foreach (var fpdbPath in EnumerateFingerprintDbPaths())
        {
            anyBackendFilePresent = true;
            try
            {
                var xml = File.ReadAllText(fpdbPath);
                if (!string.IsNullOrWhiteSpace(xml) && FingerprintDbHasFlagOn(xml))
                {
                    anyFlagStillOn = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DetectStateAsync: could not read {fpdbPath}: {ex.Message}");
            }
        }

        if (!anyBackendFilePresent) return WhitelistState.NotApplicable;
        return anyFlagStillOn ? WhitelistState.NotApplied : WhitelistState.Applied;
    }

    /// <summary>Counts <c>"Disable_*_Override": true</c> occurrences in raw JSON (read-only twin of
    /// <see cref="FlipDisableOverrideFlags"/> — same regex, but matches <c>true</c> and counts).</summary>
    public static int CountTrueJsonDisableFlags(string json)
    {
        int count = 0;
        foreach (var key in DisableOverrideKeys)
        {
            var pattern = $"(\"{System.Text.RegularExpressions.Regex.Escape(key)}\"\\s*:\\s*)true";
            count += System.Text.RegularExpressions.Regex.Matches(
                json, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        }
        return count;
    }

    /// <summary>
    /// Flips every <c>&lt;Disable_*_Override&gt;1&lt;/...&gt;</c> to <c>0</c> by targeted text
    /// replacement, exactly like the reference script.
    /// v0.0.44: replaces an XDocument.Parse + XmlWriter round-trip that (a) threw and changed
    /// NOTHING whenever fingerprint.db was not strict XML, and (b) when it did parse, rewrote
    /// the ENTIRE document (Indent=false collapsed formatting, self-closing tags normalized) —
    /// far more mutation than the one-byte edit NVIDIA's file needs.
    /// </summary>
    public static string FlipFingerprintDbFlags(string content, out int flagsFlipped)
    {
        int flipped = 0;
        var result = content;
        foreach (var key in DisableOverrideKeys)
        {
            var esc = System.Text.RegularExpressions.Regex.Escape(key);
            // Tolerates attributes and inner whitespace: <Key ...> 1 </Key>
            var pattern = $"(<{esc}(?:\\s[^>]*)?>\\s*)1(\\s*</{esc}>)";
            result = System.Text.RegularExpressions.Regex.Replace(
                result, pattern,
                m => { flipped++; return m.Groups[1].Value + "0" + m.Groups[2].Value; },
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        flagsFlipped = flipped;
        return result;
    }

    /// <summary>True if any <c>&lt;Disable_*_Override&gt;1&lt;/...&gt;</c> remains ON. Text-based —
    /// read-only twin of <see cref="FlipFingerprintDbFlags"/>, so detection and apply agree even
    /// on files that are not strict XML (the old XDocument version reported "no flags on" for an
    /// unparseable file, which surfaced as a FALSE "whitelist applied").</summary>
    private static bool FingerprintDbHasFlagOn(string content)
    {
        FlipFingerprintDbFlags(content, out int wouldFlip);
        return wouldFlip > 0;
    }

    /// <summary>
    /// Enumerates every fingerprint.db under the NvBackend root, RECURSIVELY.
    /// v0.0.44: previously this only looked at two hardcoded locations
    /// (ApplicationOntology\data and DAO\**). The reference script recurses the whole
    /// NvBackend tree — NVIDIA has moved/added these files across App and driver releases
    /// (581.80 notably), so any fingerprint.db outside our two guesses kept its
    /// Disable_*_Override flags ON and the whitelist stayed effectively off.
    /// </summary>
    private static IEnumerable<string> EnumerateFingerprintDbPaths()
    {
        if (!Directory.Exists(NvBackendBasePath))
            yield break;

        string[] found;
        try
        {
            found = Directory.GetFiles(NvBackendBasePath, "fingerprint.db", SearchOption.AllDirectories);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"EnumerateFingerprintDbPaths: recursive search failed: {ex.Message}");
            yield break;
        }

        foreach (var db in found)
            yield return db;
    }

    public async Task<(bool Success, string? ErrorMessage)> RestartNvidiaServicesAsync(CancellationToken ct = default)
    {
        var errors = new List<string>();

        // Try NVDisplay.ContainerLocalSystem first, then NvContainerLocalSystem
        var services = new[] { "NVDisplay.ContainerLocalSystem", "NvContainerLocalSystem" };

        foreach (var serviceName in services)
        {
            try
            {
                var stopResult = await RunNetCommandAsync("stop", serviceName, ct);
                if (!stopResult)
                {
                    Debug.WriteLine($"RestartNvidiaServicesAsync: net stop {serviceName} may have failed");
                }

                // Small delay between stop and start
                await Task.Delay(500, ct);

                var startResult = await RunNetCommandAsync("start", serviceName, ct);
                if (!startResult)
                {
                    Debug.WriteLine($"RestartNvidiaServicesAsync: net start {serviceName} may have failed");
                }
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 5)
            {
                errors.Add("Administrator access is required to restart NVIDIA services.");
                Debug.WriteLine($"RestartNvidiaServicesAsync: access denied for {serviceName}");
                break;
            }
            catch (Win32Exception ex)
            {
                errors.Add($"Failed to restart {serviceName}: {ex.Message}");
                Debug.WriteLine($"RestartNvidiaServicesAsync: {serviceName} error: {ex.Message}");
            }
            catch (Exception ex)
            {
                errors.Add($"Error restarting {serviceName}: {ex.Message}");
                Debug.WriteLine($"RestartNvidiaServicesAsync: {serviceName} unexpected error: {ex.Message}");
            }
        }

        if (errors.Count == 0)
            return (true, null);

        return (false, string.Join(" ", errors));
    }

    /// <summary>
    /// Flips every <c>"Disable_*_Override":true</c> to <c>:false</c> in the raw JSON text,
    /// whitespace-tolerant around the colon. Mirrors the reference PowerShell script's
    /// plain string replacement and makes NO assumption about the document's root shape
    /// (NVIDIA App's ApplicationStorage.json root is an object, not an array).
    /// </summary>
    public static string FlipDisableOverrideFlags(string json, out int flagsFlipped)
    {
        int flipped = 0;
        var result = json;
        foreach (var key in DisableOverrideKeys)
        {
            var pattern = $"(\"{System.Text.RegularExpressions.Regex.Escape(key)}\"\\s*:\\s*)true";
            result = System.Text.RegularExpressions.Regex.Replace(
                result, pattern,
                m => { flipped++; return m.Groups[1].Value + "false"; },
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
        flagsFlipped = flipped;
        return result;
    }

    /// <summary>
    /// Flips <c>"IsOpsSupported":false</c> → <c>true</c> for every app entry NVIDIA has actually
    /// identified (<c>"CmsId"</c> non-zero), and returns the app names it changed.
    /// </summary>
    /// <remarks>
    /// WHY: NVIDIA App gates its DLSS Override UI on this flag. Titles for which NVIDIA never
    /// published Optimal Playable Settings ("OPS") ship <c>IsOpsSupported:false</c> and show
    /// "not supported" — even after the five <c>Disable_*_Override</c> flags are all false.
    /// Star Citizen is the canonical case: real CmsId, detected by the App, permanently gated.
    /// Evidence: on a real 53-app ApplicationStorage.json every app with a working override UI
    /// had <c>IsOpsSupported:true</c> (35/35, no counterexamples), and the only independent tool
    /// that models this field (innerthoughtgames/dlss-override-plus v2.7.5, KEY_SPECS) also
    /// targets <c>true</c>. NVIDIA does not document it.
    ///
    /// CmsId gate: entries with <c>"CmsId":0</c> are user-added bare executables NVIDIA cannot
    /// identify — claiming OPS support for those is meaningless, so they are skipped. This is
    /// also why the edit must be per-app rather than a whole-file replace.
    ///
    /// ponytail: regex over per-app text slices, not a real JSON parse — matches the existing
    /// text-replacement strategy in this file (and the reference script's) and avoids rewriting
    /// a file NVIDIA App re-reads. Switch to a parsed round-trip only if we ever need to write
    /// nested values.
    /// </remarks>
    public static string FlipIsOpsSupported(string json, out List<string> appsFlipped)
    {
        var flipped = new List<string>();
        var sb = new System.Text.StringBuilder(json.Length);

        // Slice the document on app-entry boundaries so CmsId/IsOpsSupported/DisplayName are
        // evaluated per entry. "LocalId" opens each element of the Applications array.
        var bounds = System.Text.RegularExpressions.Regex
            .Matches(json, "\"LocalId\"\\s*:", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(m => m.Index)
            .ToList();

        if (bounds.Count == 0)
        {
            appsFlipped = flipped;
            return json;
        }

        sb.Append(json, 0, bounds[0]); // preamble before the first entry
        for (int i = 0; i < bounds.Count; i++)
        {
            int start = bounds[i];
            int end = (i + 1 < bounds.Count) ? bounds[i + 1] : json.Length;
            var slice = json.Substring(start, end - start);

            // Skip entries NVIDIA cannot identify (no CMS record).
            var cms = System.Text.RegularExpressions.Regex.Match(
                slice, "\"CmsId\"\\s*:\\s*(\\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            bool identified = cms.Success && cms.Groups[1].Value.TrimStart('0').Length > 0;

            if (identified)
            {
                var updated = System.Text.RegularExpressions.Regex.Replace(
                    slice, "(\"IsOpsSupported\"\\s*:\\s*)false", m => m.Groups[1].Value + "true",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (!string.Equals(updated, slice, StringComparison.Ordinal))
                {
                    var name = System.Text.RegularExpressions.Regex.Match(
                        slice, "\"DisplayName\"\\s*:\\s*\"([^\"]*)\"",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    flipped.Add(name.Success ? name.Groups[1].Value : "(unnamed)");
                    slice = updated;
                }
            }

            sb.Append(slice);
        }

        appsFlipped = flipped;
        return sb.ToString();
    }

    /// <summary>
    /// Counts app entries that <see cref="FlipIsOpsSupported"/> would change — read-only twin,
    /// sharing the exact same function so detection and apply cannot disagree (the v0.0.44 lesson).
    /// </summary>
    public static int CountUnlockableApps(string json)
    {
        FlipIsOpsSupported(json, out var apps);
        return apps.Count;
    }

    /// <summary>
    /// Unlocks NVIDIA App's DLSS Override UI for identified games it reports as "not supported"
    /// by setting <c>IsOpsSupported:true</c>. Writes a <c>.bak</c> next to the file first.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT part of <see cref="ApplyWhitelistAsync"/>: the five Disable_*_Override
    /// flags are what every comparable tool writes, whereas IsOpsSupported is undocumented and
    /// asserts a capability NVIDIA's CMS says the title lacks. Different risk class ⇒ its own
    /// opt-in action, so it can never ride along silently with the safe operation.
    /// NVIDIA App may restore this flag on CMS sync / library change — re-run if that happens.
    /// </remarks>
    public async Task<WhitelistResult> UnlockUnsupportedGamesAsync(CancellationToken ct = default)
    {
        var modifiedFiles = new List<string>();

        if (!File.Exists(ApplicationStoragePath))
            return new WhitelistResult(false, 0, "ApplicationStorage.json not found — is the NVIDIA App installed?", modifiedFiles, IsApplicable: false);

        try
        {
            var attrs = File.GetAttributes(ApplicationStoragePath);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(ApplicationStoragePath, attrs & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex)
        {
            return new WhitelistResult(false, 0,
                $"Could not clear the read-only attribute on ApplicationStorage.json: {ex.Message}. Run as Administrator and try again.",
                modifiedFiles);
        }

        string json;
        try
        {
            json = await File.ReadAllTextAsync(ApplicationStoragePath, ct);
        }
        catch (Exception ex)
        {
            return new WhitelistResult(false, 0, $"Could not read ApplicationStorage.json: {ex.Message}", modifiedFiles);
        }

        if (string.IsNullOrWhiteSpace(json))
            return new WhitelistResult(false, 0, "ApplicationStorage.json is empty.", modifiedFiles);

        var updated = FlipIsOpsSupported(json, out var appsFlipped);
        if (appsFlipped.Count == 0)
            return new WhitelistResult(true, 0, null, modifiedFiles);

        // Undocumented field on a file NVIDIA App rewrites — always leave a rollback point.
        try
        {
            File.Copy(ApplicationStoragePath, ApplicationStoragePath + ".bak", overwrite: true);
        }
        catch (Exception ex)
        {
            return new WhitelistResult(false, 0, $"Could not create a backup, so nothing was changed: {ex.Message}", modifiedFiles);
        }

        try
        {
            await File.WriteAllTextAsync(ApplicationStoragePath, updated,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            modifiedFiles.Add(ApplicationStoragePath);
            Debug.WriteLine($"UnlockUnsupportedGamesAsync: set IsOpsSupported=true for {appsFlipped.Count} app(s): {string.Join(", ", appsFlipped)}");
            return new WhitelistResult(true, appsFlipped.Count, null, modifiedFiles);
        }
        catch (UnauthorizedAccessException)
        {
            return new WhitelistResult(false, 0,
                "Could not write ApplicationStorage.json (access denied). Run the app as Administrator and try again.",
                modifiedFiles);
        }
        catch (Exception ex)
        {
            return new WhitelistResult(false, 0, $"Failed to write ApplicationStorage.json: {ex.Message}", modifiedFiles);
        }
    }

    private async Task<(bool Success, int GamesModified, string? ErrorMessage, List<string> ModifiedFiles, bool IsApplicable)> ModifyApplicationStorageJsonAsync(CancellationToken ct)
    {
        var modifiedFiles = new List<string>();

        if (!File.Exists(ApplicationStoragePath))
        {
            return (false, 0, $"ApplicationStorage.json not found at: {ApplicationStoragePath}", modifiedFiles, false);
        }

        // Clear the ReadOnly attribute NVIDIA App sets on this file, otherwise the
        // write below throws UnauthorizedAccessException and the whitelist silently
        // never applies. Mirrors the reference script's Set IsReadOnly = $false.
        try
        {
            var attrs = File.GetAttributes(ApplicationStoragePath);
            if ((attrs & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(ApplicationStoragePath, attrs & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ModifyApplicationStorageJsonAsync: could not clear ReadOnly: {ex.Message}");
        }

        string jsonContent;
        try
        {
            jsonContent = await File.ReadAllTextAsync(ApplicationStoragePath, ct);
        }
        catch (Exception ex)
        {
            return (false, 0, $"Could not read ApplicationStorage.json: {ex.Message}", modifiedFiles, true);
        }

        if (string.IsNullOrWhiteSpace(jsonContent))
        {
            return (false, 0, "ApplicationStorage.json is empty.", modifiedFiles, true);
        }

        // Operate on the raw text exactly like the reference PowerShell script
        // (JPersson77's nVAppApp script): flip every "Disable_X_Override":true to
        // :false. We do NOT parse/assume a root shape — NVIDIA App's
        // ApplicationStorage.json root is a JSON OBJECT (a wrapper around the game
        // list), not an array, so the previous JsonDocument-as-array approach bailed
        // with "root is not an array" and changed nothing. Whitespace-tolerant so it
        // matches both compact and pretty-printed variants.
        var original = jsonContent;
        var updated = FlipDisableOverrideFlags(jsonContent, out int flagsFlipped);

        if (flagsFlipped == 0)
        {
            // Nothing was set to true — already whitelisted (or no entries). Not an error.
            Debug.WriteLine("ModifyApplicationStorageJsonAsync: no Disable_*_Override flags were true");
            return (true, 0, null, modifiedFiles, true);
        }

        if (string.Equals(updated, original, StringComparison.Ordinal))
        {
            return (true, 0, null, modifiedFiles, true);
        }

        try
        {
            // UTF-8 without BOM, matching the reference script's Utf8NoBomEncoding. The default
            // File.WriteAllTextAsync encoding is also BOM-less UTF-8, but pin it explicitly —
            // NVIDIA App fails to parse this file if a BOM appears.
            await File.WriteAllTextAsync(ApplicationStoragePath, updated,
                new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false), ct);
            modifiedFiles.Add(ApplicationStoragePath);
            Debug.WriteLine($"ModifyApplicationStorageJsonAsync: flipped {flagsFlipped} override flag(s) to false");
            return (true, flagsFlipped, null, modifiedFiles, true);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, 0,
                "Could not write ApplicationStorage.json (access denied). Run the app as Administrator and try again.",
                modifiedFiles, true);
        }
        catch (Exception ex)
        {
            return (false, 0, $"Failed to write ApplicationStorage.json: {ex.Message}", modifiedFiles, true);
        }
    }

    private (bool Success, int GamesModified, string? ErrorMessage, List<string> ModifiedFiles, bool IsApplicable) ModifyFingerprintDatabases()
    {
        var modifiedFiles = new List<string>();
        int gamesModified = 0;
        string? errorMessage = null;

        // Single source of truth for which fingerprint.db files exist (shared with DetectStateAsync).
        var fpdbPaths = EnumerateFingerprintDbPaths().ToList();

        if (fpdbPaths.Count == 0)
        {
            Debug.WriteLine("ModifyFingerprintDatabases: no fingerprint.db files found");
            return (true, 0, null, modifiedFiles, false);
        }

        foreach (var fpdbPath in fpdbPaths)
        {
            try
            {
                var result = ModifySingleFingerprintDb(fpdbPath);
                if (result.Modified)
                {
                    gamesModified++;
                    modifiedFiles.Add(fpdbPath);
                }
                if (!string.IsNullOrEmpty(result.ErrorMessage) && string.IsNullOrEmpty(errorMessage))
                {
                    errorMessage = result.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ModifyFingerprintDatabases: error processing {fpdbPath}: {ex.Message}");
                if (string.IsNullOrEmpty(errorMessage))
                    errorMessage = $"Error processing {fpdbPath}: {ex.Message}";
            }
        }

        return (string.IsNullOrEmpty(errorMessage), gamesModified, errorMessage, modifiedFiles, true);
    }

    private static (bool Modified, string? ErrorMessage) ModifySingleFingerprintDb(string fpdbPath)
    {
        if (!File.Exists(fpdbPath))
            return (false, null);

        // NVIDIA App sets fingerprint.db read-only precisely to keep it from being edited —
        // the reference script clears the flag, edits, then RE-ARMS it. Clearing must happen
        // before the read/write, or the write throws and the whitelist silently no-ops.
        try
        {
            var pre = File.GetAttributes(fpdbPath);
            if ((pre & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(fpdbPath, pre & ~FileAttributes.ReadOnly);
        }
        catch (Exception ex)
        {
            return (false, $"Could not clear ReadOnly on {fpdbPath}: {ex.Message}");
        }

        string content;
        try
        {
            content = File.ReadAllText(fpdbPath);
        }
        catch (Exception ex)
        {
            return (false, $"Could not read {fpdbPath}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(content))
            return (false, null);

        var updated = FlipFingerprintDbFlags(content, out int flagsFlipped);

        if (flagsFlipped == 0)
        {
            Debug.WriteLine($"ModifySingleFingerprintDb: all override flags already 0 in {fpdbPath}");
            // Still re-arm read-only — NVIDIA relies on it and we may have just cleared it.
            TrySetReadOnly(fpdbPath);
            return (false, null);
        }

        try
        {
            // UTF-8 without BOM, matching the reference script's WriteAllLines + Utf8NoBomEncoding.
            File.WriteAllText(fpdbPath, updated, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // Re-arm read-only so NVIDIA App does not rewrite the flags back to 1.
            TrySetReadOnly(fpdbPath);

            Debug.WriteLine($"ModifySingleFingerprintDb: flipped {flagsFlipped} flag(s) in {fpdbPath}");
            return (true, null);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, $"Access denied writing {fpdbPath}. Run as Administrator and try again.");
        }
        catch (Exception ex)
        {
            return (false, $"Error writing {fpdbPath}: {ex.Message}");
        }
    }

    private static void TrySetReadOnly(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if ((attrs & FileAttributes.ReadOnly) == 0)
                File.SetAttributes(path, attrs | FileAttributes.ReadOnly);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"TrySetReadOnly: {path} — {ex.Message}");
        }
    }

    private static async Task<bool> RunNetCommandAsync(string action, string serviceName, CancellationToken ct)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "net",
            Arguments = $"{action} {serviceName}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
                return false;

            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RunNetCommandAsync: net {action} {serviceName} failed: {ex.Message}");
            return false;
        }
    }
}