namespace DLSSVersionToolkit.Core.Models;

public enum OperationStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    RolledBack
}

public class UpgradeOperation
{
    public string OperationId { get; set; } = Guid.NewGuid().ToString();
    public string SourceType { get; set; } = "";
    public string TargetType { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public OperationStatus Status { get; set; } = OperationStatus.Pending;
    public string BackupPath { get; set; } = "";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string ErrorMessage { get; set; } = "";
    public List<string> FilesCopied { get; set; } = new();
}