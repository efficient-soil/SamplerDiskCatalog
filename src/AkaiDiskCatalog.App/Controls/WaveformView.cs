using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using AkaiDiskCatalog.Core.Filesystem.Models;

namespace AkaiDiskCatalog.App.Controls;

/// <summary>
/// Draws a peak (min/max-per-pixel-column) waveform for one or two channels, with loop
/// start/end markers. A single O(n) linear scan per render is cheap even at hundreds of
/// thousands of frames (floppy-disk-era samples are well under a million), so no
/// incremental downsampling cache is needed.
/// </summary>
public sealed class WaveformView : Control
{
    public static readonly StyledProperty<short[]?> LeftSamplesProperty =
        AvaloniaProperty.Register<WaveformView, short[]?>(nameof(LeftSamples));

    public static readonly StyledProperty<short[]?> RightSamplesProperty =
        AvaloniaProperty.Register<WaveformView, short[]?>(nameof(RightSamples));

    public static readonly StyledProperty<IReadOnlyList<AkaiLoopInfo>?> LoopsProperty =
        AvaloniaProperty.Register<WaveformView, IReadOnlyList<AkaiLoopInfo>?>(nameof(Loops));

    public short[]? LeftSamples
    {
        get => GetValue(LeftSamplesProperty);
        set => SetValue(LeftSamplesProperty, value);
    }

    public short[]? RightSamples
    {
        get => GetValue(RightSamplesProperty);
        set => SetValue(RightSamplesProperty, value);
    }

    public IReadOnlyList<AkaiLoopInfo>? Loops
    {
        get => GetValue(LoopsProperty);
        set => SetValue(LoopsProperty, value);
    }

    static WaveformView()
    {
        AffectsRender<WaveformView>(LeftSamplesProperty, RightSamplesProperty, LoopsProperty, BoundsProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var bounds = Bounds;
        double width = bounds.Width;
        double height = bounds.Height;
        if (width <= 0 || height <= 0) return;

        var left = LeftSamples;
        if (left is null || left.Length == 0) return;

        var right = RightSamples;
        bool stereo = right is not null && right.Length > 0;

        IBrush waveBrush = TryGetResourceBrush("SubtleTextBrush") ?? Brushes.Gray;
        IBrush loopBrush = TryGetResourceBrush("AppAccentBrush") ?? Brushes.OrangeRed;
        var wavePen = new Pen(waveBrush, 1);
        var loopPen = new Pen(loopBrush, 1.5) { DashStyle = DashStyle.Dash };

        if (stereo)
        {
            double gap = 4;
            double laneHeight = (height - gap) / 2;
            DrawLane(context, wavePen, left, 0, laneHeight, width);
            DrawLane(context, wavePen, right!, laneHeight + gap, laneHeight, width);
        }
        else
        {
            DrawLane(context, wavePen, left, 0, height, width);
        }

        DrawLoopMarkers(context, loopPen, left.Length, width, height);
    }

    private static void DrawLane(DrawingContext context, Pen pen, short[] samples, double top, double laneHeight, double width)
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

    private void DrawLoopMarkers(DrawingContext context, Pen pen, int sampleCount, double width, double height)
    {
        var loops = Loops;
        if (loops is null || loops.Count == 0 || sampleCount == 0) return;

        var loop = loops[0];
        int loopEnd = Math.Clamp(loop.At, 0, sampleCount);
        int loopStart = Math.Clamp(loopEnd - loop.LengthSamples, 0, sampleCount);
        if (loopStart >= loopEnd) return;

        double startX = (double)loopStart / sampleCount * width;
        double endX = (double)loopEnd / sampleCount * width;
        context.DrawLine(pen, new Point(startX, 0), new Point(startX, height));
        context.DrawLine(pen, new Point(endX, 0), new Point(endX, height));
    }

    private IBrush? TryGetResourceBrush(string key) =>
        this.TryFindResource(key, out var value) && value is IBrush brush ? brush : null;
}
