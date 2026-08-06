using System;

namespace AkaiDiskCatalog.Core.Filesystem.Audio;

/// <summary>
/// Renders a bounded "play with loop" preview: the attack once, then the loop region
/// repeated a fixed number of extra times, then it stops. There's no streaming/seekable
/// audio API in play here (playback goes through a simple whole-file OS-native player), so
/// true infinite looping isn't attempted - this is a finite, good-enough preview.
/// </summary>
public static class LoopedPlaybackRenderer
{
    /// <summary>
    /// AKAI loop points are stored as (endSample, lengthSamples) - <paramref name="loopEndSample"/>
    /// is where playback jumps back to, and the loop start is <paramref name="loopEndSample"/> -
    /// <paramref name="lengthSamples"/>. (Verified empirically: for real active loops, end-length
    /// always lands within [0, sampleCount); end+length routinely exceeds the sample entirely.)
    /// </summary>
    public static short[] RenderWithLoopRepeats(short[] samples, int loopEndSample, int lengthSamples, int extraRepeats = 8)
    {
        if (samples.Length == 0 || lengthSamples <= 0) return samples;

        int loopEnd = Math.Clamp(loopEndSample, 0, samples.Length);
        int loopStart = loopEnd - lengthSamples;
        if (loopStart < 0 || loopStart >= loopEnd) return samples; // degenerate - fall back to plain playback

        int loopLength = loopEnd - loopStart;
        var result = new short[loopEnd + loopLength * extraRepeats];

        Array.Copy(samples, 0, result, 0, loopEnd);
        int pos = loopEnd;
        for (int i = 0; i < extraRepeats; i++)
        {
            Array.Copy(samples, loopStart, result, pos, loopLength);
            pos += loopLength;
        }

        return result;
    }
}
