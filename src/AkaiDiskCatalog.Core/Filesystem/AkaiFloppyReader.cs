using System;
using System.Collections.Generic;
using System.Text;
using AkaiDiskCatalog.Core.Filesystem.Models;

namespace AkaiDiskCatalog.Core.Filesystem;

/// <summary>
/// Reads the AKAI S900/S1000/S3000 floppy filesystem out of a raw linear block image
/// (as produced by <see cref="Hfe.HfeDecoder"/> or a plain .img file). The S900 and S1000
/// floppy layout embeds a single volume directly in the floppy header; the S3000 floppy
/// layout adds an extended directory area immediately after the header.
/// </summary>
public static class AkaiFloppyReader
{
    private const int BlockSize = 0x0400; // 1024 bytes
    private const int VoldirEntrySize = 0x18; // 24 bytes
    private const int VoldirEntriesInHeader = 64;
    private const int FatEntriesLowDensity = 0x320;  // 800 blocks  (800KB)
    private const int FatEntriesHighDensity = 0x640;  // 1600 blocks (1.6MB)
    private const ushort FatFree = 0x0000;
    private const ushort FatSystem = 0x4000;
    private const ushort FatEndOfChain = 0xC000;

    public static AkaiVolume ReadFloppyVolume(byte[] image, DiskDensity density, List<string> warnings)
    {
        int fatEntries = density == DiskDensity.LowDensity800K ? FatEntriesLowDensity : FatEntriesHighDensity;
        int headerBlocks = density == DiskDensity.LowDensity800K ? 4 : 5;

        int fatOffset = VoldirEntriesInHeader * VoldirEntrySize; // 1536
        int labelOffset = fatOffset + fatEntries * 2;

        if (image.Length < headerBlocks * BlockSize)
            throw new InvalidOperationException("Image too small to contain an AKAI floppy header.");

        var fat = new ushort[fatEntries];
        for (int i = 0; i < fatEntries; i++)
            fat[i] = BitConverter.ToUInt16(image, fatOffset + i * 2);

        string volName = AkaiCharset.DecodeName(image, labelOffset, 12, s900: false);
        ushort osVerRaw = BitConverter.ToUInt16(image, labelOffset + 14);
        string osVer = FormatOsVersion(osVerRaw);

        var entries = ParseVoldirEntries(image, 0, VoldirEntriesInHeader);

        // Detect S3000 floppy: first entry's type byte is the sentinel 0xFF, and the
        // real file table lives in an extended directory area after the header.
        bool isS3000 = entries.Count > 0 && entries[0].TypeByte == 0xFF;

        var volume = new AkaiVolume
        {
            Name = volName,
            OsVersion = osVer,
            Platform = isS3000 ? AkaiPlatform.S3000 : AkaiPlatform.S1000,
        };

        List<AkaiFileEntry> fileEntries;
        if (isS3000)
        {
            int dirStartBlock = density == DiskDensity.LowDensity800K ? 4 : 5;
            int dirOffset = dirStartBlock * BlockSize;
            const int s3000Entries = 510;
            if (image.Length >= dirOffset + s3000Entries * VoldirEntrySize)
            {
                fileEntries = ParseVoldirEntries(image, dirOffset, s3000Entries);
            }
            else
            {
                warnings.Add("S3000 floppy directory area truncated or missing.");
                fileEntries = new List<AkaiFileEntry>();
            }
        }
        else
        {
            fileEntries = entries;
        }

        foreach (var fe in fileEntries)
        {
            if (fe.TypeByte == 0x00 || fe.TypeByte == 0xFF) continue; // free / sentinel
            volume.Files.Add(fe);
        }

        return volume;
    }

    private static List<AkaiFileEntry> ParseVoldirEntries(byte[] image, int baseOffset, int count)
    {
        var result = new List<AkaiFileEntry>(count);
        for (int i = 0; i < count; i++)
        {
            int off = baseOffset + i * VoldirEntrySize;
            if (off + VoldirEntrySize > image.Length) break;
            byte type = image[off + 16];
            if (type == 0x00) continue; // free entry, skip

            var (platform, kind) = AkaiFileType.Classify(type);
            string name = AkaiCharset.DecodeName(image, off, 12, s900: platform == AkaiPlatform.S900);
            int size = image[off + 17] | (image[off + 18] << 8) | (image[off + 19] << 16);
            int start = BitConverter.ToUInt16(image, off + 20);
            ushort osverRaw = BitConverter.ToUInt16(image, off + 22);

            result.Add(new AkaiFileEntry
            {
                Name = name,
                TypeByte = type,
                Platform = platform,
                Kind = kind,
                SizeBytes = size,
                StartBlock = start,
                OsVersion = platform == AkaiPlatform.S900 ? "" : FormatOsVersion(osverRaw),
                DirectoryEntryOffset = off,
            });
        }
        return result;
    }

    /// <summary>
    /// Follows the FAT chain starting at <paramref name="startBlock"/> and returns up to
    /// <paramref name="sizeBytes"/> of file data. Also usable to fetch just the first block
    /// cheaply via <paramref name="maxBytes"/>.
    /// </summary>
    public static byte[] ReadFileData(byte[] image, DiskDensity density, int startBlock, int sizeBytes, int maxBytes = int.MaxValue)
    {
        int fatEntries = density == DiskDensity.LowDensity800K ? FatEntriesLowDensity : FatEntriesHighDensity;
        int fatOffset = VoldirEntriesInHeader * VoldirEntrySize;

        int wanted = Math.Min(sizeBytes, maxBytes);
        var outBuf = new byte[wanted];
        int written = 0;
        int block = startBlock;
        int safety = fatEntries + 4;

        while (written < wanted && safety-- > 0)
        {
            if (block < 0 || block >= fatEntries) break;
            int blockOff = block * BlockSize;
            if (blockOff + BlockSize > image.Length) break;

            int copyLen = Math.Min(BlockSize, wanted - written);
            Buffer.BlockCopy(image, blockOff, outBuf, written, copyLen);
            written += copyLen;

            if (written >= wanted) break;

            ushort fatVal = BitConverter.ToUInt16(image, fatOffset + block * 2);
            if (fatVal == FatEndOfChain || fatVal == FatSystem || fatVal == FatFree) break;
            block = fatVal;
        }

        if (written < outBuf.Length)
            Array.Resize(ref outBuf, written);
        return outBuf;
    }

    /// <summary>
    /// Walks the same FAT chain as <see cref="ReadFileData"/> and overwrites the bytes at
    /// logical file offset [<paramref name="logicalOffset"/>, +<paramref name="newBytes"/>.Length)
    /// in place, splitting the write across a block boundary if the target range straddles
    /// non-contiguous blocks. Returns false (without guaranteeing no partial write) if the
    /// chain doesn't reach the requested range.
    /// </summary>
    public static bool WriteFileBytes(byte[] image, DiskDensity density, int startBlock, int sizeBytes, int logicalOffset, byte[] newBytes)
    {
        int rangeEnd = logicalOffset + newBytes.Length;
        if (rangeEnd > sizeBytes) return false;

        int fatEntries = density == DiskDensity.LowDensity800K ? FatEntriesLowDensity : FatEntriesHighDensity;
        int fatOffset = VoldirEntriesInHeader * VoldirEntrySize;

        int block = startBlock;
        int logicalPos = 0;
        int safety = fatEntries + 4;

        while (logicalPos < rangeEnd && safety-- > 0)
        {
            if (block < 0 || block >= fatEntries) return false;
            int blockOff = block * BlockSize;
            if (blockOff + BlockSize > image.Length) return false;

            int blockLogicalStart = logicalPos;
            int blockLogicalEnd = logicalPos + BlockSize;

            int ovStart = Math.Max(blockLogicalStart, logicalOffset);
            int ovEnd = Math.Min(blockLogicalEnd, rangeEnd);
            if (ovStart < ovEnd)
            {
                int srcOffsetInNewBytes = ovStart - logicalOffset;
                int destOffsetInBlock = ovStart - blockLogicalStart;
                Buffer.BlockCopy(newBytes, srcOffsetInNewBytes, image, blockOff + destOffsetInBlock, ovEnd - ovStart);
            }

            logicalPos = blockLogicalEnd;
            if (logicalPos >= rangeEnd) break;

            ushort fatVal = BitConverter.ToUInt16(image, fatOffset + block * 2);
            if (fatVal == FatEndOfChain || fatVal == FatSystem || fatVal == FatFree) return logicalPos >= rangeEnd;
            block = fatVal;
        }

        return logicalPos >= rangeEnd;
    }

    /// <summary>
    /// OS version is stored as two separate bytes, each holding a plain decimal number
    /// (not raw*100 arithmetic): high byte = major version, low byte = minor version.
    /// E.g. 0x0428 -> "4.40", 0x091e -> "9.30", 0x1100 -> "17.00".
    /// </summary>
    private static string FormatOsVersion(ushort raw)
    {
        if (raw == 0) return "";
        int major = raw >> 8;
        int minor = raw & 0xFF;
        return $"{major}.{minor:D2}";
    }
}
