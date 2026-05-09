namespace DLSSVersionToolkit.Core.Models;

public class VersionInfo
{
    public string Version { get; set; } = "";
    public string Source { get; set; } = "";

    public VersionInfo() { }
    public VersionInfo(string version, string source)
    {
        Version = version;
        Source = source;
    }
}