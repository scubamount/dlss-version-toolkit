namespace DLSSVersionToolkit.Core.Models;

/// <summary>
/// One locally-imported DLL that the user has asserted over whatever the download channel would
/// otherwise install.
/// </summary>
public class OverrideRecord
{
    /// <summary>Canonical DLL file name, e.g. <c>nvngx_dlssd.dll</c>.</summary>
    public string DllName { get; set; } = "";

    /// <summary>Version read from the DLL's own bytes at import time, e.g. <c>310.7.128.0</c>.</summary>
    public string Version { get; set; } = "";

    /// <summary>SHA-256 of the imported DLL. The manifest is a claim; this is how we verify it.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Absolute path the DLL was imported FROM (the library folder copy).</summary>
    public string SourcePath { get; set; } = "";

    /// <summary>Packed NGX version folder the import landed in, e.g. <c>20318080</c>.</summary>
    public string PackedFolder { get; set; } = "";

    /// <summary>Whether the import targeted the Staging tree.</summary>
    public bool Staging { get; set; }

    public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// The set of active local overrides, persisted next to the app's other state.
///
/// WHY THIS EXISTS (v0.0.52). Before this, an imported DLL was indistinguishable from a downloaded
/// one the moment the import finished — the app had no record that the user had asserted a
/// preference. Every downstream problem followed from that single gap: the UI could not mark an
/// overridden component, Update All could not preserve one, and nothing could tell the user when a
/// published release had finally caught up with their manual import. One record fixes all three.
/// </summary>
public class OverrideManifest
{
    /// <summary>Folder the user drops importable DLLs into. Empty = the app default.</summary>
    public string LibraryPath { get; set; } = "";

    public List<OverrideRecord> Overrides { get; set; } = new();
}
