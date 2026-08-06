using System;

namespace AkaiDiskCatalog.Core.Filesystem.Audio;

/// <summary>
/// Builds a minimal, dependency-free 16-bit PCM WAV byte stream - just a 44-byte RIFF/WAVE
/// header in front of the same sample bytes already in memory, no re-encoding. Used both for
/// sample preview playback and (potentially) a future "export to WAV" feature.
/// </summary>
public static class WavWriter
{
    public static byte[] WriteMono(short[] samples, int sampleRateHz) =>
        Build(sampleRateHz, numChannels: 1, samples);

    public static byte[] WriteStereoInterleaved(short[] left, short[] right, int sampleRateHz)
    {
        int frames = Math.Min(left.Length, right.Length);
        var interleaved = new short[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            interleaved[i * 2] = left[i];
            interleaved[i * 2 + 1] = right[i];
        }
        return Build(sampleRateHz, numChannels: 2, interleaved);
    }

    private static byte[] Build(int sampleRateHz, int numChannels, short[] interleavedSamples)
    {
        const int bitsPerSample = 16;
        int blockAlign = numChannels * bitsPerSample / 8;
        int byteRate = sampleRateHz * blockAlign;
        int dataSize = interleavedSamples.Length * 2;
        int fileSize = 36 + dataSize;

        var buf = new byte[44 + dataSize];
        int pos = 0;

        WriteAscii(buf, ref pos, "RIFF");
        WriteI32(buf, ref pos, fileSize);
        WriteAscii(buf, ref pos, "WAVE");

        WriteAscii(buf, ref pos, "fmt ");
        WriteI32(buf, ref pos, 16); // fmt chunk size
        WriteI16(buf, ref pos, 1);  // PCM
        WriteI16(buf, ref pos, (short)numChannels);
        WriteI32(buf, ref pos, sampleRateHz);
        WriteI32(buf, ref pos, byteRate);
        WriteI16(buf, ref pos, (short)blockAlign);
        WriteI16(buf, ref pos, bitsPerSample);

        WriteAscii(buf, ref pos, "data");
        WriteI32(buf, ref pos, dataSize);
        for (int i = 0; i < interleavedSamples.Length; i++)
            WriteI16(buf, ref pos, interleavedSamples[i]);

        return buf;
    }

    private static void WriteAscii(byte[] buf, ref int pos, string s)
    {
        for (int i = 0; i < s.Length; i++) buf[pos++] = (byte)s[i];
    }

    private static void WriteI32(byte[] buf, ref int pos, int value)
    {
        buf[pos++] = (byte)value;
        buf[pos++] = (byte)(value >> 8);
        buf[pos++] = (byte)(value >> 16);
        buf[pos++] = (byte)(value >> 24);
    }

    private static void WriteI16(byte[] buf, ref int pos, short value)
    {
        buf[pos++] = (byte)value;
        buf[pos++] = (byte)(value >> 8);
    }
}
