using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DLSSVersionToolkit.Core.Services;

public interface IDlssIndicatorService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
    /// <summary>Raw DWORD currently stored, or null if the value/key is absent.</summary>
    int? GetRawValue();
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

    public int? GetRawValue()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegSubKey);
            var value = key?.GetValue(RegValueName, null);
            return value is int i ? i : (int?)null;
        }
        catch
        {
            return null;
        }
    }

    public bool IsEnabled()
    {
        var raw = GetRawValue();
        return raw.HasValue && raw.Value != 0;
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
