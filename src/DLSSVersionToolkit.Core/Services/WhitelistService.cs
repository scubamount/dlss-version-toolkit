using System.ComponentModel;
namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Xml.Linq;

public sealed record WhitelistResult(
    bool Success,
    int GamesModified,
    string? ErrorMessage,
    List<string> ModifiedFiles,
    bool IsApplicable = true);

public interface IWhitelistService
{
    Task<WhitelistResult> ApplyWhitelistAsync(CancellationToken ct = default);
    Task<(bool Success, string? ErrorMessage)> RestartNvidiaServicesAsync(CancellationToken ct = default);
}

public sealed class WhitelistService : IWhitelistService
{
    private static readonly string NvBackendBasePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NVIDIA Corporation", "NVIDIA app", "NvBackend");

    private static readonly string ApplicationStoragePath = Path.Combine(NvBackendBasePath, "ApplicationStorage.json");

    private static readonly string ApplicationOntologyPath = Path.Combine(NvBackendBasePath, "ApplicationOntology", "data", "fingerprint.db");

    private static readonly string DaoBasePath = Path.Combine(NvBackendBasePath, "DAO");

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
            await File.WriteAllTextAsync(ApplicationStoragePath, updated, ct);
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

        // Primary fingerprint.db location
        var fpdbPaths = new List<string>();

        if (File.Exists(ApplicationOntologyPath))
            fpdbPaths.Add(ApplicationOntologyPath);

        // Search DAO subdirectories for additional fingerprint.db copies
        if (Directory.Exists(DaoBasePath))
        {
            try
            {
                var daoDbs = Directory.GetFiles(DaoBasePath, "fingerprint.db", SearchOption.AllDirectories);
                foreach (var db in daoDbs)
                {
                    if (!fpdbPaths.Contains(db, StringComparer.OrdinalIgnoreCase))
                        fpdbPaths.Add(db);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ModifyFingerprintDatabases: error searching DAO directory: {ex.Message}");
            }
        }

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

        string xmlContent;
        try
        {
            xmlContent = File.ReadAllText(fpdbPath);
        }
        catch (Exception ex)
        {
            return (false, $"Could not read {fpdbPath}: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(xmlContent))
            return (false, null);

        try
        {
            var doc = XDocument.Parse(xmlContent);

            bool anyNeedChange = false;
            foreach (var tagName in DisableOverrideKeys)
            {
                var elements = doc.Descendants(tagName).ToList();
                foreach (var el in elements)
                {
                    if (el.Value == "1")
                    {
                        anyNeedChange = true;
                        break;
                    }
                }
                if (anyNeedChange)
                    break;
            }

            if (!anyNeedChange)
            {
                Debug.WriteLine($"ModifySingleFingerprintDb: all override flags already 0 in {fpdbPath}");
                return (false, null);
            }

            // Apply changes
            foreach (var tagName in DisableOverrideKeys)
            {
                var elements = doc.Descendants(tagName).ToList();
                foreach (var el in elements)
                {
                    if (el.Value == "1")
                        el.Value = "0";
                }
            }

            // Write as UTF-8 without BOM
            var writerSettings = new System.Xml.XmlWriterSettings
            {
                Indent = false,
                Encoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                OmitXmlDeclaration = false
            };

            using var stream = new FileStream(fpdbPath, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = System.Xml.XmlWriter.Create(stream, writerSettings);
            doc.Save(writer);

            // Set Read-Only to prevent NVIDIA from reverting
            var attrs = File.GetAttributes(fpdbPath);
            if ((attrs & FileAttributes.ReadOnly) == 0)
            {
                File.SetAttributes(fpdbPath, attrs | FileAttributes.ReadOnly);
            }

            Debug.WriteLine($"ModifySingleFingerprintDb: modified {fpdbPath}");
            return (true, null);
        }
        catch (System.Xml.XmlException ex)
        {
            return (false, $"XML parse error in {fpdbPath}: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Error writing {fpdbPath}: {ex.Message}");
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