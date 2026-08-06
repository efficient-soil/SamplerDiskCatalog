using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace AkaiDiskCatalog.App.Converters;

/// <summary>
/// Computes a zoomable control's rendered width as (viewport width) * (zoom factor), so at
/// 1x it always exactly fills whatever space the host actually has - self-adjusting to window
/// size and sibling layout changes instead of a fixed magic-number width that can drift out of
/// sync (as happened when the pitch slider column ate into the loop editor's available width).
/// </summary>
public sealed class ZoomWidthConverter : IMultiValueConverter
{
    public static readonly ZoomWidthConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is not [double viewportWidth, double zoomFactor] || viewportWidth <= 0)
            return AvaloniaProperty.UnsetValue; // viewport not measured yet - let Width stay unset for this pass

        return viewportWidth * zoomFactor;
    }
}
