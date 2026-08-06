using System.Collections.Generic;
using AkaiDiskCatalog.Core.Filesystem.Audio;
using AkaiDiskCatalog.Core.Filesystem.Models;

namespace AkaiDiskCatalog.App.Models;

/// <summary>
/// Dumb data projection over a successful <see cref="SampleAudioResult"/> - no logic, no
/// service dependency, matching <see cref="SelectedFileDetail"/>'s existing style.
/// </summary>
public sealed class SampleAudioViewModel
{
    public SampleAudioViewModel(SampleAudioResult result)
    {
        Left = result.Left;
        Right = result.Right;
        IsStereo = result.IsStereo;
        PartnerName = result.PartnerName;
        SampleRateHz = result.SampleRateHz;
        Loops = result.Loops;
        PlaybackMode = result.PlaybackMode;
    }

    public short[] Left { get; }
    public short[]? Right { get; }
    public bool IsStereo { get; }
    public string? PartnerName { get; }
    public int SampleRateHz { get; }
    public IReadOnlyList<AkaiLoopInfo> Loops { get; }
    public string? PlaybackMode { get; }

    /// <summary>True only when a loop is both present and actually active on real hardware
    /// (PlaybackMode LOOP/LOOP-NOT-RELEASE) - stale loop-header bytes left over from a
    /// previously-looped, now-NOLOOP sample must not offer a "play with loop" button.</summary>
    public bool HasLoop => Loops.Count > 0 && (PlaybackMode == "LOOP" || PlaybackMode == "LOOP-NOT-RELEASE");
}
