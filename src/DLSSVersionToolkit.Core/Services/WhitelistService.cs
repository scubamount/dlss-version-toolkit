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

    private async Task<(bool Success, int GamesModified, string? ErrorMessage, List<string> ModifiedFiles, bool IsApplicable)> ModifyApplicationStorageJsonAsync(CancellationToken ct)
    {
        var modifiedFiles = new List<string>();
        int gamesModified = 0;

        if (!File.Exists(ApplicationStoragePath))
        {
            return (false, 0, $"ApplicationStorage.json not found at: {ApplicationStoragePath}", modifiedFiles, false);
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

        try
        {
            using var document = JsonDocument.Parse(jsonContent);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return (false, 0, "ApplicationStorage.json root is not an array.", modifiedFiles, true);
            }

            // Check if any overrides need changing
            bool anyNeedChange = false;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var key in DisableOverrideKeys)
                {
                    if (element.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.True)
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
                Debug.WriteLine("ModifyApplicationStorageJsonAsync: all override flags already false");
            return (true, 0, null, modifiedFiles, true);
            }
        }
        catch (JsonException ex)
        {
            return (false, 0, $"Failed to parse ApplicationStorage.json: {ex.Message}", modifiedFiles, true);
        }

        // Re-parse for modification
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            // Build a mutable representation
            var gameObjects = new List<Dictionary<string, object?>>();

            foreach (var element in root.EnumerateArray())
            {
                var dict = new Dictionary<string, object?>();
                foreach (var prop in element.EnumerateObject())
                {
                    dict[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Number => prop.Value.GetDouble(),
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Null => null,
                        _ => prop.Value.Clone()
                    };
                }
                gameObjects.Add(dict);
            }

            // Modify the flags
            foreach (var game in gameObjects)
            {
                bool modified = false;
                foreach (var key in DisableOverrideKeys)
                {
                    if (game.TryGetValue(key, out var val) && val is true)
                    {
                        game[key] = false;
                        modified = true;
                    }
                }
                if (modified)
                    gamesModified++;
            }

            // Serialize back
            var options = new JsonSerializerOptions { WriteIndented = true };
            var newJson = JsonSerializer.Serialize(gameObjects, options);
            await File.WriteAllTextAsync(ApplicationStoragePath, newJson, ct);

            modifiedFiles.Add(ApplicationStoragePath);
            Debug.WriteLine($"ModifyApplicationStorageJsonAsync: modified {gamesModified} game entries");
        return (true, gamesModified, null, modifiedFiles, true);
        }
        catch (JsonException ex)
        {
            return (false, 0, $"Failed to re-serialize ApplicationStorage.json: {ex.Message}", modifiedFiles, true);
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