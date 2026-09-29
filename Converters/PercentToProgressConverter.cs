using System;
using System.Globalization;
using Microsoft.Maui.Controls;

namespace CarScanner.Converters;

public class PercentToProgressConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int percent)
        {
            return Math.Clamp(percent / 100.0, 0.0, 1.0);
        }

        if (value is double d)
        {
            return Math.Clamp(d / 100.0, 0.0, 1.0);
        }

        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return 0.0;
    }
}