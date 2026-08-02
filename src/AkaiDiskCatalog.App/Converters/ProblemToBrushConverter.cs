using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace AkaiDiskCatalog.App.Converters;

public sealed class ProblemToBrushConverter : IValueConverter
{
    public static readonly ProblemToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isProblem = value is true;
        var key = isProblem ? "WarningBrush" : "OkBrush";
        if (Avalonia.Application.Current?.TryGetResource(key, Avalonia.Application.Current.ActualThemeVariant, out var brush) == true)
            return brush;
        return Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
