namespace DLSSVersionToolkit.Core.Models;

public class ScanResult
{
    public List<DLSSVersionEntry> Sources { get; set; } = new();
    public Dictionary<string, VersionInfo> NewestPerComponent { get; set; } = new();
    public List<Recommendation> Recommendations { get; set; } = new();
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;
    public TimeSpan Duration { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();

    public bool HasErrors => Errors.Count > 0;
    public bool HasWarnings => Warnings.Count > 0;
}