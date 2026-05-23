using System.Runtime.Versioning;
using Microsoft.Win32;

namespace DLSSVersionToolkit.Core.Services;

public interface IDlssIndicatorService
{
    bool IsEnabled();
    void SetEnabled(bool enabled);
}

[SupportedOSPlatform("windows")]
public class DlssIndicatorService : IDlssIndicatorService
{
    private const string RegSubKey = @"SOFTWARE\NVIDIA Corporation\Global\NGXCore";
    private const string RegValueName = "ShowDlssIndicator";

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegSubKey);
            if (key == null) return false;
            var value = key.GetValue(RegValueName, 0);
            return value is int intValue && intValue != 0;
        }
        catch
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegSubKey, writable: true)
                ?? throw new InvalidOperationException("Failed to open NGXCore registry key.");
            key.SetValue(RegValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Administrator access is required to change the DLSS Indicator. " +
                "Restart the app as Administrator and try again.");
        }
    }
}
