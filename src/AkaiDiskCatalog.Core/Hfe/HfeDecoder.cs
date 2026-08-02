using System;
using System.Collections.Generic;
using System.IO;

namespace AkaiDiskCatalog.Core.Hfe;

/// <summary>
/// Result of decoding a raw MFM floppy image (.hfe) into linear sector blocks.
/// </summary>
public sealed class HfeDecodeResult
{
    /// <summary>Decoded blocks keyed by (cylinder, head, sector-1based).</summary>
    public Dictionary<(int Cyl, int Head, int Sector), byte[]> Sectors { get; } = new();

    public int Cylinders { get; set; }
    public int Heads { get; set; }
    public int SectorsPerTrack { get; set; }
    public int SectorSize { get; set; }

    public List<(int Cyl, int Head, int Sector)> MissingSectors { get; } = new();

    /// <summary>
    /// Assemble sectors into a single linear byte array in (cyl outer, head, sector) order,
    /// matching the conventional AKAI floppy block-linear layout. Missing sectors are
    /// filled with zero bytes.
    /// </summary>
    public byte[] ToLinearImage()
    {
        var result = new byte[Cylinders * Heads * SectorsPerTrack * SectorSize];
        int offset = 0;
        for (int c = 0; c < Cylinders; c++)
        {
            for (int h = 0; h < Heads; h++)
            {
                for (int s = 1; s <= SectorsPerTrack; s++)
                {
                    if (Sectors.TryGetValue((c, h, s), out var buf))
                    {
                        Buffer.BlockCopy(buf, 0, result, offset, SectorSize);
                    }
                    offset += SectorSize;
                }
            }
        }
        return result;
    }
}

/// <summary>
/// Decodes HxC Floppy Emulator (.hfe) bitstream images of standard IBM-style MFM
/// double-density floppies (as used by the AKAI S900/S1000/S1100/S3000 samplers)
/// into raw sector data, without any external tools.
/// </summary>
public static class HfeDecoder
{
    // 0xA1 sync mark encoded in MFM (with the deliberate clock-bit violation),
    // expressed as the 16 physical bit-cells in chronological order.
    private static readonly byte[] Sync16 = ParseBits("0100010010001001");

    private static byte[] ParseBits(string s)
    {
        var arr = new byte[s.Length];
        for (int i = 0; i < s.Length; i++) arr[i] = (byte)(s[i] - '0');
        return arr;
    }

    public static HfeDecodeResult Decode(string path, int expectedCylinders = 80, int expectedHeads = 2, int expectedSectorsPerTrack = 10, int sectorSize = 1024)
    {
        var data = File.ReadAllBytes(path);
        if (data.Length < 8 || data[0] != 'H' || data[1] != 'X' || data[2] != 'C')
            throw new InvalidDataException("Not a valid HFE file (missing HXCPICFE signature).");

        string sig = System.Text.Encoding.ASCII.GetString(data, 0, 8);
        if (sig != "HXCPICFE" && sig != "HXCHFEV3")
            throw new InvalidDataException($"Unsupported HFE signature '{sig}'.");

        int numTrack = data[9];
        int numSide = data[10];
        ushort trackListOffsetBlocks = BitConverter.ToUInt16(data, 0x12);
        int lutOffset = trackListOffsetBlocks * 512;

        var result = new HfeDecodeResult
        {
            Cylinders = expectedCylinders,
            Heads = expectedHeads,
            SectorsPerTrack = expectedSectorsPerTrack,
            SectorSize = sectorSize
        };

        for (int t = 0; t < numTrack; t++)
        {
            int entryOff = lutOffset + t * 4;
            if (entryOff + 4 > data.Length) break;
            ushort blockOff = BitConverter.ToUInt16(data, entryOff);
            ushort trackLen = BitConverter.ToUInt16(data, entryOff + 2);
            if (trackLen == 0) continue;
            int off = blockOff * 512;

            var (side0Bits, side1Bits) = ExtractTrackBits(data, off, trackLen);

            DecodeSideIntoSectors(side0Bits, result.Sectors);
            if (numSide > 1)
                DecodeSideIntoSectors(side1Bits, result.Sectors);
        }

        for (int c = 0; c < result.Cylinders; c++)
            for (int h = 0; h < result.Heads; h++)
                for (int s = 1; s <= result.SectorsPerTrack; s++)
                    if (!result.Sectors.ContainsKey((c, h, s)))
                        result.MissingSectors.Add((c, h, s));

        return result;
    }

    private static (byte[] side0, byte[] side1) ExtractTrackBits(byte[] data, int off, int trackLen)
    {
        // The HFE track buffer interleaves 256-byte chunks: side0, side1, side0, side1, ...
        var side0Bytes = new List<byte>(trackLen / 2 + 256);
        var side1Bytes = new List<byte>(trackLen / 2 + 256);
        int i = 0;
        int end = Math.Min(trackLen, data.Length - off);
        while (i < end)
        {
            int chunk0Len = Math.Min(256, end - i);
            for (int k = 0; k < chunk0Len; k++) side0Bytes.Add(data[off + i + k]);
            i += 256;
            if (i >= end) break;
            int chunk1Len = Math.Min(256, end - i);
            for (int k = 0; k < chunk1Len; k++) side1Bytes.Add(data[off + i + k]);
            i += 256;
        }
        return (BytesToBitsLsbFirst(side0Bytes), BytesToBitsLsbFirst(side1Bytes));
    }

    private static byte[] BytesToBitsLsbFirst(List<byte> bytes)
    {
        var bits = new byte[bytes.Count * 8];
        for (int i = 0; i < bytes.Count; i++)
        {
            byte b = bytes[i];
            for (int bit = 0; bit < 8; bit++)
            {
                bits[i * 8 + bit] = (byte)((b >> bit) & 1);
            }
        }
        return bits;
    }

    /// <summary>
    /// Scans one side's chronological bit stream for ID+DATA address-mark pairs and
    /// fills in decoded sector data.
    /// </summary>
    private static void DecodeSideIntoSectors(byte[] bits, Dictionary<(int, int, int), byte[]> sectors)
    {
        int n = bits.Length;
        (int c, int h, int r, int n2)? pendingId = null;

        // Rolling 48-bit window over the last 48 bits seen, MSB-first shift register style.
        ulong window = 0;
        const ulong mask48 = (1UL << 48) - 1;
        ulong syncPattern = BitsToUlong(Sync16, Sync16, Sync16);

        for (int pos = 0; pos < n; pos++)
        {
            window = ((window << 1) | bits[pos]) & mask48;
            if (pos < 47) continue;
            if (window != syncPattern) continue;

            int markStart = pos + 1; // bit right after the 48-bit sync sequence
            byte[]? markBytes = DecodeMfmBytes(bits, markStart, 1);
            if (markBytes == null) continue;
            byte mtype = markBytes[0];

            if (mtype == 0xFE) // ID address mark
            {
                var idField = DecodeMfmBytes(bits, markStart, 1 + 4 + 2);
                if (idField != null)
                {
                    int c = idField[1], h = idField[2], r = idField[3], nn = idField[4];
                    pendingId = (c, h, r, nn);
                }
            }
            else if (mtype == 0xFB || mtype == 0xF8) // Data / deleted-data address mark
            {
                if (pendingId is { } id)
                {
                    int secLen = id.n2 <= 7 ? (128 << id.n2) : 1024;
                    var dataField = DecodeMfmBytes(bits, markStart, 1 + secLen + 2);
                    if (dataField != null && dataField.Length == 1 + secLen + 2)
                    {
                        var payload = new byte[secLen];
                        Array.Copy(dataField, 1, payload, 0, secLen);
                        sectors[(id.c, id.h, id.r)] = payload;
                    }
                    pendingId = null;
                }
            }
        }
    }

    private static ulong BitsToUlong(params byte[][] groups)
    {
        ulong v = 0;
        foreach (var g in groups)
            foreach (var b in g)
                v = (v << 1) | b;
        return v;
    }

    /// <summary>
    /// Decodes <paramref name="nbytes"/> MFM-encoded bytes starting at bit index <paramref name="start"/>.
    /// Each encoded byte occupies 16 bit-cells; the data bits are at the odd positions
    /// (clock bits at even positions) within each 16-bit group.
    /// </summary>
    private static byte[]? DecodeMfmBytes(byte[] bits, int start, int nbytes)
    {
        long needed = (long)nbytes * 16;
        if (start + needed > bits.Length) return null;
        var outBytes = new byte[nbytes];
        int pos = start;
        for (int i = 0; i < nbytes; i++)
        {
            byte b = 0;
            for (int bitPair = 0; bitPair < 8; bitPair++)
            {
                // bitPair*2 = clock bit, bitPair*2+1 = data bit
                byte dataBit = bits[pos + bitPair * 2 + 1];
                b = (byte)((b << 1) | dataBit);
            }
            outBytes[i] = b;
            pos += 16;
        }
        return outBytes;
    }
}
