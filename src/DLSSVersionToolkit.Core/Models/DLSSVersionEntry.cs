namespace DLSSVersionToolkit.Core.Models;

public class DLSSVersionEntry
{
    public string Source { get; set; } = "Unknown";
    public string BuildID { get; set; } = "";
    public string DLSS { get; set; } = "Unknown";
    public string FrameGen { get; set; } = "Unknown";
    public string DLSSD { get; set; } = "Unknown";
    public string DeepDVC { get; set; } = "Unknown";
    public string DLSSNR { get; set; } = "Unknown";
    public string Streamline { get; set; } = "Unknown";
    public string Path { get; set; } = "";
    public bool IsNewestDLSS { get; set; }
    public bool IsNewestFG { get; set; }
    public bool IsNewestDLSSD { get; set; }
    public bool IsNewestDeepDVC { get; set; }
    public bool IsNewestDLSSNR { get; set; }

    /// <summary>
    /// Components on this row currently supplied by a locally-imported override rather than the
    /// download channel, e.g. { "nvngx_dlssd.dll" }. Drives the 🔒 marker in the versions grid.
    /// </summary>
    public List<string> OverriddenDlls { get; set; } = new();

    /// <summary>True when any component on this row is locally overridden.</summary>
    public bool HasOverride => OverriddenDlls.Count > 0;

    /// <summary>
    /// Marker shown in the grid's Override column. Empty string rather than a dash so unmarked
    /// rows stay visually quiet.
    /// </summary>
    public string OverrideMarker => HasOverride ? "🔒" : "";

    /// <summary>Tooltip naming exactly which components are overridden.</summary>
    public string OverrideTooltip => HasOverride
        ? "Locally imported override: " + string.Join(", ", OverriddenDlls)
        : "";

    public DateTime ScannedAt { get; set; } = DateTime.UtcNow;

    public string DisplaySource => Source switch
    {
        "NGX_Release" => "NGX Release",
        "NGX_Staging" => "NGX Staging",
        "NGX_OTA" => "NVIDIA OTA cache",
        "NGX_OTA_Staging" => "NVIDIA OTA cache (pre-release)",
        "AnWave" => "AnWave",
        "StreamlineSDK" => "Streamline SDK",
        _ => Source
    };
}