using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace AkaiDiskCatalog.App.Controls;

/// <summary>
/// A zoomable waveform (rendered at whatever <see cref="Control.Width"/> the host sets, meant
/// to be hosted in a horizontally-scrolling ScrollViewer) with two draggable vertical markers
/// for the loop start/end sample. Unlike the read-only <see cref="WaveformView"/>, this control
/// owns pointer interaction and reports the live-dragged sample index back via two-way bound
/// LoopStart/LoopEnd properties - there's no separate "commit" step, the ViewModel sees the
/// value update as the user drags.
/// </summary>
public sealed class LoopWaveformEditControl : Control
{
    private const double HitToleranceScreenPx = 8;

    public static readonly StyledProperty<short[]?> LeftSamplesProperty =
        AvaloniaProperty.Register<LoopWaveformEditControl, short[]?>(nameof(LeftSamples));

    public static readonly StyledProperty<short[]?> RightSamplesProperty =
        AvaloniaProperty.Register<LoopWaveformEditControl, short[]?>(nameof(RightSamples));

    public static readonly StyledProperty<int> LoopStartProperty =
        AvaloniaProperty.Register<LoopWaveformEditControl, int>(nameof(LoopStart), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<int> LoopEndProperty =
        AvaloniaProperty.Register<LoopWaveformEditControl, int>(nameof(LoopEnd), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

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

    public int LoopStart
    {
        get => GetValue(LoopStartProperty);
        set => SetValue(LoopStartProperty, value);
    }

    public int LoopEnd
    {
        get => GetValue(LoopEndProperty);
        set => SetValue(LoopEndProperty, value);
    }

    static LoopWaveformEditControl()
    {
        AffectsRender<LoopWaveformEditControl>(LeftSamplesProperty, RightSamplesProperty, LoopStartProperty, LoopEndProperty, BoundsProperty);
    }

    private enum Marker { None, Start, End }
    private Marker _dragging = Marker.None;

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
        var loopPen = new Pen(loopBrush, 2);

        if (stereo)
        {
            double gap = 4;
            double laneHeight = (height - gap) / 2;
            WaveformRendering.DrawLane(context, wavePen, left, 0, laneHeight, width);
            WaveformRendering.DrawLane(context, wavePen, right!, laneHeight + gap, laneHeight, width);
        }
        else
        {
            WaveformRendering.DrawLane(context, wavePen, left, 0, height, width);
        }

        int totalSamples = left.Length;
        double startX = SampleToX(LoopStart, totalSamples, width);
        double endX = SampleToX(LoopEnd, totalSamples, width);
        DrawMarker(context, loopPen, loopBrush, startX, height);
        DrawMarker(context, loopPen, loopBrush, endX, height);
    }

    private static void DrawMarker(DrawingContext context, Pen pen, IBrush fill, double x, double height)
    {
        context.DrawLine(pen, new Point(x, 0), new Point(x, height));
        // Small triangular handle at the top for drag affordance.
        var geo = new StreamGeometry();
        using (var gc = geo.Open())
        {
            gc.BeginFigure(new Point(x - 5, 0), true);
            gc.LineTo(new Point(x + 5, 0));
            gc.LineTo(new Point(x, 8));
            gc.EndFigure(true);
        }
        context.DrawGeometry(fill, null, geo);
    }

    private double SampleToX(int sample, int totalSamples, double width) =>
        totalSamples <= 0 ? 0 : Math.Clamp((double)sample / totalSamples * width, 0, width);

    private int XToSample(double x, double width)
    {
        int totalSamples = LeftSamples?.Length ?? 0;
        if (totalSamples <= 0 || width <= 0) return 0;
        return Math.Clamp((int)Math.Round(x / width * totalSamples), 0, totalSamples);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        double width = Bounds.Width;
        int totalSamples = LeftSamples?.Length ?? 0;
        if (totalSamples <= 0 || width <= 0) return;

        double x = e.GetPosition(this).X;
        double startX = SampleToX(LoopStart, totalSamples, width);
        double endX = SampleToX(LoopEnd, totalSamples, width);

        double distToStart = Math.Abs(x - startX);
        double distToEnd = Math.Abs(x - endX);

        if (distToStart > HitToleranceScreenPx && distToEnd > HitToleranceScreenPx) return;

        _dragging = distToStart <= distToEnd ? Marker.Start : Marker.End;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging == Marker.None) return;

        double width = Bounds.Width;
        int totalSamples = LeftSamples?.Length ?? 0;
        int sample = XToSample(e.GetPosition(this).X, width);

        if (_dragging == Marker.Start)
            LoopStart = Math.Clamp(sample, 0, Math.Max(0, LoopEnd - 1));
        else
            LoopEnd = Math.Clamp(sample, LoopStart + 1, totalSamples);

        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragging == Marker.None) return;
        _dragging = Marker.None;
        e.Pointer.Capture(null);
    }

    private IBrush? TryGetResourceBrush(string key) =>
        this.TryFindResource(key, out var value) && value is IBrush brush ? brush : null;
}
