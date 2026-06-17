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