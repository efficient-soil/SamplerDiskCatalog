using System;
using System.IO;
using System.Linq;
using AkaiDiskCatalog.Core.Filesystem.Models;

namespace AkaiDiskCatalog.Core.Filesystem.Audio;

public enum SampleLoopWriteFailureReason
{
    None,
    FileNotFound,
    UnsupportedFile,
    InvalidLoopRange,
    IoError,
}

/// <summary>
/// PlaybackMode is one of "LOOP", "LOOP-NOT-RELEASE", or "NOLOOP" (matches
/// <see cref="AkaiSampleParser"/>'s decode order; "PLAY-TO-END" isn't offered here since
/// this editor only creates/adjusts loops, never that fourth mode).
/// When PlaybackMode is "NOLOOP", LoopStartSample/LoopEndSample/Hold/TimeMsIfNotHold are
/// ignored and only the playback-mode byte is patched - any existing loop bytes are left
/// untouched, matching the stale-loop-bytes-with-NOLOOP pattern already observed on real disks.
/// </summary>
public sealed record SampleLoopWriteRequest(
    string SourcePath,
    int StartBlock,
    byte TypeByte,
    int LoopStartSample,
    int LoopEndSample,
    bool Hold,
    int TimeMsIfNotHold,
    string PlaybackMode);

public sealed record SampleLoopWriteResult(
    bool Success,
    SampleLoopWriteFailureReason FailureReason,
    string? ErrorDetail,
    string? NewImagePath,
    bool PartnerAlsoUpdated);

/// <summary>
/// Writes a sample's loop point(s) into a copy of an AKAI disk image. Mirrors
/// <see cref="AkaiDiskWriter.RenameFile"/>'s pattern: reload the disk fresh, relocate the
/// target entry by (StartBlock, TypeByte), patch bytes in memory, write to a new .img -
/// the source file passed in is never modified.
/// </summary>
public static class AkaiSampleLoopWriter
{
    private const int LoopTableOffset = 38; // loop slot 0 within the 150-byte sample header
    private const int NumLoopsOffset = 16;
    private const int FirstLoopIndexOffset = 17;
    private const int PlaybackModeOffset = 19;
    private const short HoldTimeMs = 9999;

    private static readonly string[] PlaybackModes = { "LOOP", "LOOP-NOT-RELEASE", "NOLOOP" };

    public static SampleLoopWriteResult WriteLoop(SampleLoopWriteRequest request)
    {
        byte[] image;
        DiskDensity density;
        AkaiVolume volume;

        try
        {
            var (linearImage, dens, warnings, _, _) = DiskImageLoader.LoadLinearImage(request.SourcePath);
            image = linearImage;
            density = dens;
            volume = AkaiFloppyReader.ReadFloppyVolume(image, density, warnings);
        }
        catch (Exception ex)
        {
            return Fail(SampleLoopWriteFailureReason.IoError, $"Could not re-read the source disk: {ex.Message}");
        }

        var entry = volume.Files.FirstOrDefault(f =>
            f.StartBlock == request.StartBlock && f.TypeByte == request.TypeByte && f.Kind == AkaiFileKind.Sample);
        if (entry is null)
        {
            return Fail(SampleLoopWriteFailureReason.FileNotFound,
                "This sample could no longer be found on the source disk (it may have changed since it was last scanned). Try rescanning.");
        }

        if (entry.Platform != AkaiPlatform.S1000)
        {
            return Fail(SampleLoopWriteFailureReason.UnsupportedFile,
                $"Loop editing is only supported for S1000 samples right now (this is {entry.Platform}).");
        }

        int modeIndex = Array.IndexOf(PlaybackModes, request.PlaybackMode);
        if (modeIndex < 0)
        {
            return Fail(SampleLoopWriteFailureReason.UnsupportedFile, $"Unknown playback mode '{request.PlaybackMode}'.");
        }

        if (!ApplyLoopToEntry(image, density, entry, request, modeIndex, out var failReason, out var failDetail))
        {
            return Fail(failReason, failDetail);
        }

        bool partnerUpdated = false;
        string? partnerName = AkaiSampleAudioLoader.FindStereoPartnerName(entry.Name);
        if (partnerName is not null)
        {
            var partnerEntry = volume.Files.FirstOrDefault(f =>
                f.Kind == AkaiFileKind.Sample && f.Platform == entry.Platform &&
                string.Equals(f.Name, partnerName, StringComparison.Ordinal));
            if (partnerEntry is not null)
            {
                if (!ApplyLoopToEntry(image, density, partnerEntry, request, modeIndex, out failReason, out failDetail))
                {
                    return Fail(failReason, failDetail);
                }
                partnerUpdated = true;
            }
        }

        string outPath;
        try
        {
            outPath = AkaiDiskWriter.BuildOutputPath(request.SourcePath);
            File.WriteAllBytes(outPath, image);
        }
        catch (Exception ex)
        {
            return Fail(SampleLoopWriteFailureReason.IoError, ex.Message);
        }

        return new SampleLoopWriteResult(true, SampleLoopWriteFailureReason.None, null, outPath, partnerUpdated);
    }

    private static bool ApplyLoopToEntry(
        byte[] image, DiskDensity density, AkaiFileEntry entry, SampleLoopWriteRequest request, int modeIndex,
        out SampleLoopWriteFailureReason failReason, out string? failDetail)
    {
        failReason = SampleLoopWriteFailureReason.None;
        failDetail = null;

        byte[] header = AkaiFloppyReader.ReadFileData(image, density, entry.StartBlock, entry.SizeBytes, maxBytes: AkaiSampleParser.HeaderSize);
        if (header.Length < AkaiSampleParser.HeaderSize)
        {
            failReason = SampleLoopWriteFailureReason.IoError;
            failDetail = $"\"{entry.Name}\": sample header is truncated on disk.";
            return false;
        }

        bool disablingLoop = request.PlaybackMode == "NOLOOP";
        if (!disablingLoop)
        {
            var info = AkaiSampleParser.Parse(header, out _);
            int numSamples = info?.NumSamples ?? 0;

            if (request.LoopStartSample < 0 || request.LoopEndSample <= request.LoopStartSample || request.LoopEndSample > numSamples)
            {
                failReason = SampleLoopWriteFailureReason.InvalidLoopRange;
                failDetail = $"\"{entry.Name}\": loop range {request.LoopStartSample}-{request.LoopEndSample} is invalid for a {numSamples}-sample file.";
                return false;
            }

            // Preserve the loop record's 2 reserved/fine-tune bytes if a loop already exists there,
            // default to zero otherwise (verified empirically: real loop records use 00 00 here).
            byte fine0 = 0, fine1 = 0;
            if (header[NumLoopsOffset] > 0)
            {
                fine0 = header[LoopTableOffset + 4];
                fine1 = header[LoopTableOffset + 5];
            }

            var loopRecord = new byte[12];
            int length = request.LoopEndSample - request.LoopStartSample;
            short timeMs = request.Hold ? HoldTimeMs : (short)request.TimeMsIfNotHold;
            WriteI32(loopRecord, 0, request.LoopEndSample);
            loopRecord[4] = fine0;
            loopRecord[5] = fine1;
            WriteI32(loopRecord, 6, length);
            WriteI16(loopRecord, 10, timeMs);

            if (!AkaiFloppyReader.WriteFileBytes(image, density, entry.StartBlock, entry.SizeBytes, LoopTableOffset, loopRecord) ||
                !AkaiFloppyReader.WriteFileBytes(image, density, entry.StartBlock, entry.SizeBytes, NumLoopsOffset, new byte[] { 1 }) ||
                !AkaiFloppyReader.WriteFileBytes(image, density, entry.StartBlock, entry.SizeBytes, FirstLoopIndexOffset, new byte[] { 0 }))
            {
                failReason = SampleLoopWriteFailureReason.IoError;
                failDetail = $"\"{entry.Name}\": failed to write loop data to the disk image.";
                return false;
            }
        }

        if (!AkaiFloppyReader.WriteFileBytes(image, density, entry.StartBlock, entry.SizeBytes, PlaybackModeOffset, new byte[] { (byte)modeIndex }))
        {
            failReason = SampleLoopWriteFailureReason.IoError;
            failDetail = $"\"{entry.Name}\": failed to write playback mode to the disk image.";
            return false;
        }

        return true;
    }

    private static void WriteI32(byte[] buf, int off, int value)
    {
        buf[off] = (byte)value;
        buf[off + 1] = (byte)(value >> 8);
        buf[off + 2] = (byte)(value >> 16);
        buf[off + 3] = (byte)(value >> 24);
    }

    private static void WriteI16(byte[] buf, int off, short value)
    {
        buf[off] = (byte)value;
        buf[off + 1] = (byte)(value >> 8);
    }

    private static SampleLoopWriteResult Fail(SampleLoopWriteFailureReason reason, string? detail) =>
        new(false, reason, detail, null, false);
}
