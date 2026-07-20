namespace DLSSVersionToolkit.Core.Services;

using System.Diagnostics;
using System.IO;
using System.Text.Json;

/// <summary>
/// Persisted index of DRS game-profile names (v0.0.39). The expensive part of "Apply to all
/// games" was never the writes — it was filtering ~8000 predefined driver profiles down to the
/// few hundred with applications installed (one GetProfileInfo P/Invoke + struct marshal per
/// profile, every single apply). This index caches the surviving names once; later applies
/// jump straight to FindProfileByName per cached name and skip the scan entirely.
///
/// Invalidation: the index stores the NVIDIA driver version it was built against. A driver
/// install/upgrade rewrites the profile database, so a version mismatch silently invalidates
/// the index and the next apply falls back to a full scan (which rebuilds it as a side effect).
/// No manual "please reindex" nagging needed.
/// </summary>
public class ProfileIndex
{
    public string DriverVersion { get; set; } = "";
    public DateTime IndexedAt { get; set; }
    public List<string> GameProfileNames { get; set; } = new();
}

public static class ProfileIndexStore
{
    private static readonly string DefaultPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DLSSVersionToolkit", "profile-index.json");

    /// <summary>Loads the index; returns null if missing, corrupt, or driver version mismatch.</summary>
    public static ProfileIndex? LoadValid(string currentDriverVersion, string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            if (!File.Exists(file))
                return null;

            var index = JsonSerializer.Deserialize<ProfileIndex>(File.ReadAllText(file));
            if (index == null || index.GameProfileNames.Count == 0)
                return null;

            if (!string.Equals(index.DriverVersion, currentDriverVersion, StringComparison.Ordinal))
            {
                Debug.WriteLine($"ProfileIndexStore: driver changed ({index.DriverVersion} -> {currentDriverVersion}); index invalidated.");
                return null;
            }

            return index;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ProfileIndexStore.LoadValid failed (treating as no index): {ex.Message}");
            return null;
        }
    }

    /// <summary>Persists the index. Non-fatal on failure (next apply just scans again).</summary>
    public static void Save(ProfileIndex index, string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            var dir = Path.GetDirectoryName(file);
            if (dir != null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(file, JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ProfileIndexStore.Save failed (non-fatal): {ex.Message}");
        }
    }
}
