using System;
using System.Text;
using AkaiDiskCatalog.Core.Filesystem.Models;

namespace AkaiDiskCatalog.Core.Filesystem;

/// <summary>
/// Parses AKAI S1000 program files (struct akai_program1000_s header followed by
/// struct akai_program1000kg_s keygroups, each with 4 velocity zones).
/// S3000 programs share the same 150-byte header layout (plus 42 trailing bytes we
/// skip) but use a different, larger keygroup structure that is not parsed here.
/// </summary>
public static class AkaiProgramParser
{
    public const int HeaderSize = 0x96;   // 150 bytes
    public const int KeygroupSize = 0x96; // 150 bytes
    private const int VelZoneSize = 0x18; // 24 bytes
    private const int VelZonesPerKg = 4;

    private static readonly string[] VelzonePlaybackModes =
    {
        "SAMPLE", "LOOP", "LOOP-NOT-RELEASE", "NOLOOP", "PLAY-TO-END"
    };

    public static AkaiProgramInfo? Parse(byte[] fullFileBytes, AkaiPlatform platform, out string? warning)
    {
        warning = null;
        if (fullFileBytes.Length < HeaderSize)
        {
            warning = $"Program header truncated ({fullFileBytes.Length} bytes, need {HeaderSize}).";
            return null;
        }

        byte blockId = fullFileBytes[0];
        if (blockId != 0x01)
        {
            warning = $"Unexpected program block ID 0x{blockId:X2} (expected 0x01).";
        }

        byte midich1 = fullFileBytes[16];

        var info = new AkaiProgramInfo
        {
            RamName = AkaiCharset.DecodeName(fullFileBytes, 3, 12, s900: false),
            MidiChannel = midich1 == 0xFF ? -1 : midich1 + 1,
            KeyLow = fullFileBytes[19],
            KeyHigh = fullFileBytes[20],
            OctaveOffset = unchecked((sbyte)fullFileBytes[21]),
            KeygroupCrossfade = fullFileBytes[41] != 0,
            NumKeygroups = fullFileBytes[42],
        };

        if (platform == AkaiPlatform.S3000)
        {
            // S3000 keygroup layout differs from S1000; header fields above are still valid
            // (S3000 program = S1000 header + 42 extra bytes) but we don't decode keygroups.
            info.KeygroupsUnparsed = true;
            return info;
        }

        int off = HeaderSize;
        for (int k = 0; k < info.NumKeygroups; k++)
        {
            if (off + KeygroupSize > fullFileBytes.Length)
            {
                warning = $"Program data truncated: only {k}/{info.NumKeygroups} keygroups present.";
                break;
            }

            var kg = new AkaiKeygroupInfo
            {
                KeyLow = fullFileBytes[off + 3],
                KeyHigh = fullFileBytes[off + 4],
                CentsTune = unchecked((sbyte)fullFileBytes[off + 5]),
                SemitoneTune = unchecked((sbyte)fullFileBytes[off + 6]),
                Filter = fullFileBytes[off + 7],
                VelocityCrossfade = fullFileBytes[off + 30] != 0,
            };

            int vzBase = off + 34;
            for (int v = 0; v < VelZonesPerKg; v++)
            {
                int vOff = vzBase + v * VelZoneSize;
                byte pmodeRaw = fullFileBytes[vOff + 19];
                var vz = new AkaiVelocityZoneInfo
                {
                    SampleName = AkaiCharset.DecodeName(fullFileBytes, vOff, 12, s900: false),
                    VelocityLow = fullFileBytes[vOff + 12],
                    VelocityHigh = fullFileBytes[vOff + 13],
                    CentsTune = unchecked((sbyte)fullFileBytes[vOff + 14]),
                    SemitoneTune = unchecked((sbyte)fullFileBytes[vOff + 15]),
                    Loudness = unchecked((sbyte)fullFileBytes[vOff + 16]),
                    Filter = unchecked((sbyte)fullFileBytes[vOff + 17]),
                    Pan = unchecked((sbyte)fullFileBytes[vOff + 18]),
                    PlaybackMode = VelzonePlaybackModes[Math.Min(pmodeRaw, (byte)4)],
                };
                kg.VelocityZones.Add(vz);
            }

            info.Keygroups.Add(kg);
            off += KeygroupSize;
        }

        return info;
    }
}
