namespace DLSSVersionToolkit.Core.Models;

using System.Diagnostics;
using System.Text.Json;

/// <summary>
/// One step of an Update All run: name, outcome, and human detail. Status is a small closed
/// set ("ok" / "warn" / "info" / "fail") so the UI can color a dot without parsing prose.
/// </summary>
public sealed class UpdateRunStep
{
    public string Name { get; set; } = "";
    public string Status { get; set; } = "info";
    public string Detail { get; set; } = "";
}

/// <summary>
/// A persisted record of one Update All run. Replaces the old fire-and-forget summary
/// MessageBox: evidence that vanishes on OK is not evidence (the v0.0.42 lesson — a success
/// dialog hid a sync that copied zero files for five releases). Reports are written to
/// %AppData%\DLSSVersionToolkit\runs and the last <see cref="RunReportStore.KeepCount"/> kept,
/// so "send me your last run file" is possible in bug reports.
/// </summary>
public sealed class UpdateRunReport
{
    public DateTime StartedAt { get; set; }
    public DateTime FinishedAt { get; set; }
    public string AppVersion { get; set; } = "";
    public List<UpdateRunStep> Steps { get; set; } = new();
}

public static class RunReportStore
{
    public const int KeepCount = 10;

    private static string DefaultDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DLSSVersionToolkit", "runs");

    /// <summary>
    /// Persists a report as run-yyyyMMdd-HHmmss.json and trims the directory to the newest
    /// <see cref="KeepCount"/> files. Non-fatal on any failure — a report that cannot be
    /// written must never break the run it describes. Returns the written path or null.
    /// </summary>
    public static string? Save(UpdateRunReport report, string? dir = null)
    {
        try
        {
            var target = dir ?? DefaultDir;
            Directory.CreateDirectory(target);
            var path = Path.Combine(target,
                $"run-{report.StartedAt:yyyyMMdd-HHmmss}-{Guid.NewGuid():N[..8]}.json");
            File.WriteAllText(path, JsonSerializer.Serialize(report,
                new JsonSerializerOptions { WriteIndented = true }));

            // Trim: newest KeepCount by name (timestamp-named, so lexical == chronological).
            var files = Directory.GetFiles(target, "run-*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < files.Length - KeepCount; i++)
                File.Delete(files[i]);

            return path;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"RunReportStore.Save failed (non-fatal): {ex.Message}");
            return null;
        }
    }
}
