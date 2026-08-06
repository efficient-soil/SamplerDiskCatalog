using System;
using System.IO;
using System.Threading.Tasks;
using AkaiDiskCatalog.App.Models;
using AkaiDiskCatalog.App.Services;
using AkaiDiskCatalog.Core.Filesystem.Audio;
using AkaiDiskCatalog.Data;
using AkaiDiskCatalog.Data.Models;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkaiDiskCatalog.App.ViewModels;

/// <summary>
/// Backs the dedicated loop-editing window. Self-contained: owns its own playback service
/// (decoupled from the main window's Play/Stop state) and its own disk-write flow, mirroring
/// MainViewModel's rename flow (reload disk fresh, write to a new .img, re-catalog via
/// ScanService.ScanFile) but scoped to just this one dialog's lifetime.
/// </summary>
public partial class LoopEditorViewModel : ObservableObject, IDisposable
{
    private const double BaseWidth = 900;

    private readonly ScanService _scanner;
    private readonly FileSearchResult _source;
    private readonly SamplePlaybackService _playback = new();

    public string Name => _source.Name;
    public short[] Left { get; }
    public short[]? Right { get; }
    public bool IsStereo { get; }
    public int SampleRateHz { get; }
    public int TotalSamples { get; }

    public LoopEditorViewModel(ScanService scanner, FileSearchResult source, SampleAudioViewModel audio)
    {
        _scanner = scanner;
        _source = source;
        Left = audio.Left;
        Right = audio.Right;
        IsStereo = audio.IsStereo;
        SampleRateHz = audio.SampleRateHz;
        TotalSamples = audio.Left.Length;

        _playback.PlaybackStopped += (_, _) => Dispatcher.UIThread.Post(() => IsPlayingPreview = false);

        if (audio.Loops.Count > 0)
        {
            var loop = audio.Loops[0];
            int end = Math.Clamp(loop.At, 0, TotalSamples);
            int start = Math.Clamp(end - loop.LengthSamples, 0, end);
            if (start >= end) { start = 0; end = TotalSamples; } // degenerate stale bytes - fall back to full range
            _loopStart = start;
            _loopEnd = end;
            _isHold = loop.TimeMs == 9999;
            _timeMsValue = _isHold ? 0 : loop.TimeMs;
        }
        else
        {
            _loopStart = 0;
            _loopEnd = TotalSamples;
            _isHold = true;
            _timeMsValue = 0;
        }

        _playbackMode = audio.PlaybackMode is "LOOP" or "LOOP-NOT-RELEASE" ? audio.PlaybackMode : "LOOP";
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopStartMs))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayPreviewCommand))]
    private int _loopStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoopEndMs))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayPreviewCommand))]
    private int _loopEnd;

    public double LoopStartMs => SampleRateHz > 0 ? LoopStart / (double)SampleRateHz * 1000 : 0;
    public double LoopEndMs => SampleRateHz > 0 ? LoopEnd / (double)SampleRateHz * 1000 : 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorWidth))]
    private double _zoomFactor = 1;

    public double EditorWidth => BaseWidth * ZoomFactor;

    [ObservableProperty] private bool _isHold;
    [ObservableProperty] private int _timeMsValue;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(PlayPreviewCommand))]
    private string _playbackMode; // "LOOP" | "LOOP-NOT-RELEASE" | "NOLOOP"

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPreviewCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopPreviewCommand))]
    private bool _isPlayingPreview;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty] private string? _saveMessage;

    /// <summary>Non-null once a save has completed successfully - the opener closes the window
    /// and refreshes the main file list when this becomes set.</summary>
    [ObservableProperty] private string? _newImagePath;

    private bool CanPlayPreview() => !IsPlayingPreview && PlaybackMode != "NOLOOP" && LoopStart < LoopEnd;

    [RelayCommand(CanExecute = nameof(CanPlayPreview))]
    private void PlayPreview()
    {
        _playback.Play(BuildPreviewWav());
        IsPlayingPreview = true;
    }

    private bool CanStopPreview() => IsPlayingPreview;

    [RelayCommand(CanExecute = nameof(CanStopPreview))]
    private void StopPreview()
    {
        _playback.Stop();
        IsPlayingPreview = false;
    }

    private byte[] BuildPreviewWav()
    {
        short[] left = LoopedPlaybackRenderer.RenderWithLoopRepeats(Left, LoopEnd, LoopEnd - LoopStart);
        if (IsStereo && Right is { } right)
        {
            short[] rightRendered = LoopedPlaybackRenderer.RenderWithLoopRepeats(right, LoopEnd, LoopEnd - LoopStart);
            return WavWriter.WriteStereoInterleaved(left, rightRendered, SampleRateHz);
        }
        return WavWriter.WriteMono(left, SampleRateHz);
    }

    private bool CanSave() => !IsSaving && (PlaybackMode == "NOLOOP" || LoopStart < LoopEnd);

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        IsSaving = true;
        SaveMessage = null;
        try
        {
            var request = new SampleLoopWriteRequest(
                _source.DiskSourcePath, _source.StartBlock, _source.TypeByte,
                LoopStart, LoopEnd, IsHold, TimeMsValue, PlaybackMode);
            var result = await Task.Run(() => AkaiSampleLoopWriter.WriteLoop(request));

            if (!result.Success)
            {
                SaveMessage = DescribeFailure(result);
                return;
            }

            _scanner.ScanFile(result.NewImagePath!);
            string partnerNote = result.PartnerAlsoUpdated ? " Both stereo channels were updated." : "";
            SaveMessage = $"Saved as a new disk image ({Path.GetFileName(result.NewImagePath)}); the original file was not modified.{partnerNote}";
            NewImagePath = result.NewImagePath;
        }
        catch (Exception ex)
        {
            SaveMessage = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static string DescribeFailure(SampleLoopWriteResult r) => r.FailureReason switch
    {
        SampleLoopWriteFailureReason.InvalidLoopRange => r.ErrorDetail ?? "Invalid loop range.",
        SampleLoopWriteFailureReason.UnsupportedFile => r.ErrorDetail ?? "This sample type can't be loop-edited.",
        SampleLoopWriteFailureReason.FileNotFound => r.ErrorDetail ?? "File not found. Try rescanning.",
        SampleLoopWriteFailureReason.IoError => $"Couldn't write the new disk image: {r.ErrorDetail}",
        _ => r.ErrorDetail ?? "Save failed.",
    };

    public void Dispose() => _playback.Dispose();
}
