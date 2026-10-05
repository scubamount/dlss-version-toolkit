using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DLSSVersionToolkit.Core.Services;

public interface IDlssIndicatorService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
    /// <summary>Raw DWORD currently stored, or null if the value/key is absent OR unreadable.</summary>
    int? GetRawValue();

    /// <summary>
    /// Like <see cref="GetRawValue"/>, but tells "absent" (true, null) apart from "could not read"
    /// (false). The Reset baseline needs the difference: recording a failed read as absent made
    /// Reset delete the user's real value (v0.77).
    /// </summary>
    bool TryGetRawValue(out int? value);

    /// <summary>
    /// Writes <paramref name="value"/> verbatim, or DELETES the value when null (v0.76 Reset).
    /// SetEnabled cannot restore a baseline: it only writes 1024/0, so an absent value came back
    /// as DWORD 0 and a custom value came back as 1024.
    /// </summary>
    void SetRawValue(int? value);
}

[SupportedOSPlatform("windows")]
public class DlssIndicatorService : IDlssIndicatorService
{
    private const string RegSubKey = @"SOFTWARE\NVIDIA Corporation\Global\NGXCore";
    private const string RegValueName = "ShowDlssIndicator";

    // The DLSS on-screen indicator overlay (DLL version / preset / render res) is activated
    // by NGXCore!ShowDlssIndicator = 0x400 (1024 decimal), NOT 1. NVIDIA's own Streamline
    // sample historically wrote 1, which does not light up the overlay on current DLSS
    // runtimes — this is the most commonly reported "indicator does nothing" cause.
    // We write 1024 to enable; any non-zero value is treated as enabled on read so a legacy
    // 1 (or a hand-edited value) still registers as "on".
    private const int EnabledValue = 1024; // 0x400
    private const int DisabledValue = 0;

    public int? GetRawValue() => TryGetRawValue(out var value) ? value : null;

    public bool TryGetRawValue(out int? value)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegSubKey);
            value = key?.GetValue(RegValueName, null) is int i ? i : null;
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"DlssIndicatorService: registry read failed: {ex.Message}");
            value = null;
            return false;
        }
    }

    public bool IsEnabled()
    {
        var raw = GetRawValue();
        return raw.HasValue && raw.Value != 0;
    }

    public void SetRawValue(int? value)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegSubKey, writable: true)
                ?? throw new InvalidOperationException("Failed to open NGXCore registry key.");
            if (value is int v)
                key.SetValue(RegValueName, v, RegistryValueKind.DWord);
            else
                key.DeleteValue(RegValueName, throwOnMissingValue: false);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Administrator access is required to change the DLSS Indicator. " +
                "Restart the app as Administrator and try again.");
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegSubKey, writable: true)
                ?? throw new InvalidOperationException("Failed to open NGXCore registry key.");
            key.SetValue(RegValueName, enabled ? EnabledValue : DisabledValue, RegistryValueKind.DWord);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Administrator access is required to change the DLSS Indicator. " +
                "Restart the app as Administrator and try again.");
        }
    }
}
