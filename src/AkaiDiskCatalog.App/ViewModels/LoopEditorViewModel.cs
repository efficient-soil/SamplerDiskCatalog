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

    [ObservableProperty] private double _zoomFactor = 1;

    [ObservableProperty] private bool _isHold;
    [ObservableProperty] private int _timeMsValue;

    /// <summary>Preview-only varispeed pitch offset in semitones - never written when saving.
    /// Applied by scaling the preview WAV's declared sample rate, so pitch and playback speed
    /// change together (like a turntable pitch fader), not a duration-preserving pitch shift.</summary>
    [ObservableProperty] private int _pitchSemitones;

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

    private bool CanPlayPreview() => !IsPlayingPreview && (PlaybackMode == "NOLOOP" || LoopStart < LoopEnd);

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
        int rate = ComputePreviewRate();

        if (PlaybackMode == "NOLOOP")
        {
            return IsStereo && Right is { } r
                ? WavWriter.WriteStereoInterleaved(Left, r, rate)
                : WavWriter.WriteMono(Left, rate);
        }

        int extraRepeats = ComputeExtraRepeats();
        short[] left = LoopedPlaybackRenderer.RenderWithLoopRepeats(Left, LoopEnd, LoopEnd - LoopStart, extraRepeats);
        if (IsStereo && Right is { } right)
        {
            short[] rightRendered = LoopedPlaybackRenderer.RenderWithLoopRepeats(right, LoopEnd, LoopEnd - LoopStart, extraRepeats);
            return WavWriter.WriteStereoInterleaved(left, rightRendered, rate);
        }
        return WavWriter.WriteMono(left, rate);
    }

    /// <summary>Varispeed: pitching up/down by scaling the declared WAV sample rate, so the OS
    /// player plays the same PCM data faster/slower - pitch and duration change together, same
    /// as a turntable pitch fader (no time-stretch DSP).</summary>
    private int ComputePreviewRate() =>
        Math.Max(1, (int)Math.Round(SampleRateHz * Math.Pow(2, PitchSemitones / 12.0)));

    /// <summary>
    /// This app's playback has no real crossfade/DSP engine, so it can't reproduce whatever a
    /// real S1000 actually does with a non-HOLD loop time - but it can at least make the
    /// "Hold" checkbox and "Time (ms)" field audibly *do something* in the preview: Hold loops
    /// a fixed bounded number of times (same convention used elsewhere in this app), while a
    /// finite time loops for approximately that many milliseconds and then stops on its own.
    /// The exact byte value is still written to the saved sample either way for real hardware
    /// or other tools to interpret.
    /// </summary>
    private int ComputeExtraRepeats()
    {
        const int holdPreviewRepeats = 8;
        const int maxRepeats = 2000; // safety cap against pathologically short loop regions

        if (IsHold) return holdPreviewRepeats;

        int loopLength = LoopEnd - LoopStart;
        if (loopLength <= 0 || SampleRateHz <= 0) return 0;

        double loopDurationMs = loopLength / (double)SampleRateHz * 1000;
        int repeats = (int)Math.Round(TimeMsValue / loopDurationMs);
        return Math.Clamp(repeats, 0, maxRepeats);
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
