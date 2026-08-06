using System;
using Avalonia;
using Avalonia.Media;

namespace AkaiDiskCatalog.App.Controls;

/// <summary>
/// Shared peak (min/max-per-pixel-column) waveform drawing, used by both the read-only
/// <see cref="WaveformView"/> and the editable loop-point control. A single O(n) linear scan
/// per render is cheap even at hundreds of thousands of frames (floppy-disk-era samples are
/// well under a million), so no incremental downsampling cache is needed.
/// </summary>
internal static class WaveformRendering
{
    public static void DrawLane(DrawingContext context, Pen pen, short[] samples, double top, double laneHeight, double width)
    {
        double mid = top + laneHeight / 2;
        double scale = laneHeight / 2 / short.MaxValue;
        int n = samples.Length;
        int columns = (int)Math.Ceiling(width);

        for (int x = 0; x < columns; x++)
        {
            int start = (int)((long)x * n / columns);
            int end = (int)((long)(x + 1) * n / columns);
            if (end <= start) end = Math.Min(start + 1, n);
            if (start >= n) break;

            short min = short.MaxValue, max = short.MinValue;
            for (int i = start; i < end; i++)
            {
                short v = samples[i];
                if (v < min) min = v;
                if (v > max) max = v;
            }

            double y1 = mid - max * scale;
            double y2 = mid - min * scale;
            if (y2 - y1 < 1) { y1 -= 0.5; y2 += 0.5; } // keep near-silent columns visible as a thin line
            context.DrawLine(pen, new Point(x + 0.5, y1), new Point(x + 0.5, y2));
        }
    }
}
