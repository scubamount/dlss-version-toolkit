namespace DLSSVersionToolkit.Core.Models;

public class DLSSVersionEntry
{
    public string Source { get; set; } = "Unknown";
    public string BuildID { get; set; } = "";
    public string DLSS { get; set; } = "Unknown";
    public string FrameGen { get; set; } = "Unknown";
    public string DLSSD { get; set; } = "Unknown";
    public string DeepDVC { get; set; } = "Unknown";
    public string Streamline { get; set; } = "Unknown";
    public string Path { get; set; } = "";
    public bool IsNewestDLSS { get; set; }
    public bool IsNewestFG { get; set; }
    public bool IsNewestDLSSD { get; set; }
    public bool IsNewestDeepDVC { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    public string DisplaySource => Source switch
    {
        "NGX_Release" => "NGX Release",
        "NGX_Staging" => "NGX Staging",
        "AnWave" => "AnWave",
        "StreamlineSDK" => "Streamline SDK",
        _ => Source
    };
}