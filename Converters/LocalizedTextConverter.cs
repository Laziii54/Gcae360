using System.Globalization;
using CarScanner.Services.Localization;

namespace CarScanner.Converters;

/// <summary>
/// Shows a stored option value ("Dark", "Metric", "Balanced") in the current language while the stored value itself stays
/// untouched. Used as the display binding of pickers: native picker items are not part of the page tree, so the label
/// translation hook never reaches them.
/// </summary>
public sealed class LocalizedTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string text ? LocalizationService.T(text) : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
