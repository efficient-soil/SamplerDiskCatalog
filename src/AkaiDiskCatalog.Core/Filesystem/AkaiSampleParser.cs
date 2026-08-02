using System;
using System.Text;
using AkaiDiskCatalog.Core.Filesystem.Models;

namespace AkaiDiskCatalog.Core.Filesystem;

/// <summary>
/// Parses the 150-byte (0x96) AKAI S1000 sample header (struct akai_sample1000_s).
/// S3000 sample files use the identical leading 150 bytes plus 42 trailing bytes we don't need.
/// </summary>
public static class AkaiSampleParser
{
    public const int HeaderSize = 0x96; // 150 bytes - enough to read from just the first block

    private static readonly string[] PlaybackModes =
    {
        "LOOP", "LOOP-NOT-RELEASE", "NOLOOP", "PLAY-TO-END"
    };

    public static AkaiSampleInfo? Parse(byte[] fileHeadBytes, out string? warning)
    {
        warning = null;
        if (fileHeadBytes.Length < HeaderSize)
        {
            warning = $"Sample header truncated ({fileHeadBytes.Length} bytes, need {HeaderSize}).";
            return null;
        }

        byte blockId = fileHeadBytes[0];
        if (blockId != 0x03)
        {
            warning = $"Unexpected sample block ID 0x{blockId:X2} (expected 0x03).";
        }

        var info = new AkaiSampleInfo
        {
            RootKey = fileHeadBytes[2],
            RamName = AkaiCharset.DecodeName(fileHeadBytes, 3, 12, s900: false),
            NumLoops = fileHeadBytes[16],
            PlaybackMode = PlaybackModes[Math.Min(fileHeadBytes[19], (byte)3)],
            CentsTune = unchecked((sbyte)fileHeadBytes[20]),
            SemitoneTune = unchecked((sbyte)fileHeadBytes[21]),
            NumSamples = ReadI32(fileHeadBytes, 26),
            SampleRateHz = BitConverter.ToUInt16(fileHeadBytes, 138),
        };

        if (info.SampleRateHz > 0)
            info.DurationMs = info.NumSamples * 1000.0 / info.SampleRateHz;

        int lfirst = fileHeadBytes[17]; // first active loop - 1 (0-based index)
        for (int i = 0; i < 8 && i < info.NumLoops; i++)
        {
            int off = 38 + i * 12;
            var loop = new AkaiLoopInfo
            {
                At = ReadI32(fileHeadBytes, off),
                LengthSamples = ReadI32(fileHeadBytes, off + 6),
                TimeMs = BitConverter.ToInt16(fileHeadBytes, off + 10),
            };
            info.Loops.Add(loop);
        }

        ushort stpaira = BitConverter.ToUInt16(fileHeadBytes, 136);
        info.IsStereoPartner = stpaira != 0xFFFF;

        return info;
    }

    private static int ReadI32(byte[] b, int off) =>
        b[off] | (b[off + 1] << 8) | (b[off + 2] << 16) | (b[off + 3] << 24);
}
