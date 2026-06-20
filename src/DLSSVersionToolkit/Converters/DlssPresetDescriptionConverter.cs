using System;
using System.Globalization;
using System.Windows.Data;
using DLSSVersionToolkit.Core.Models;

namespace DLSSVersionToolkit.Converters;

public class DlssPresetDescriptionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DlssPreset preset)
            return DlssPresetDisplay.GetDescription(preset);
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Compact preset label ("Default", "Latest", "Preset X") for the DLSS-RR and DLSS-FG
/// dropdowns, which list the full A–M range without SR-specific mode descriptions.
/// </summary>
public class DlssPresetShortLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DlssPreset preset)
            return DlssPresetDisplay.GetShortLabel(preset);
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Human-readable DLSSG generator mode label ("Dynamic", "Fixed", "Off", "Auto", "Don't change").
/// </summary>
public class DlssgModeLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is DlssgMode mode)
            return DlssPresetDisplay.GetModeLabel(mode);
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Frame multiplier label ("4x") for the DLSS-FG multiplier dropdown.
/// </summary>
public class FrameGenMultiplierLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int multiplier)
            return DlssPresetDisplay.GetMultiplierLabel(multiplier);
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}