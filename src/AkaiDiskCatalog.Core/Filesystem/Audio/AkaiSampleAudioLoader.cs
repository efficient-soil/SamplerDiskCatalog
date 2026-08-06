using System;
using System.Linq;
using AkaiDiskCatalog.Core.Filesystem.Models;

namespace AkaiDiskCatalog.Core.Filesystem.Audio;

public enum SampleAudioLoadFailureReason
{
    None,
    FileNotFound,
    UnsupportedPlatform,
    IoError,
}

public sealed record SampleAudioLoadRequest(string SourcePath, int StartBlock, byte TypeByte);

public sealed record SampleAudioResult(
    bool Success,
    SampleAudioLoadFailureReason FailureReason,
    string? ErrorDetail,
    int SampleRateHz,
    short[] Left,
    short[]? Right,
    bool IsStereo,
    string PrimaryName,
    string? PartnerName,
    System.Collections.Generic.IReadOnlyList<AkaiLoopInfo> Loops,
    string? PlaybackMode);

/// <summary>
/// Loads raw PCM audio for a sample on demand, straight from its source disk image - the
/// catalog database never stores audio, only metadata. Mirrors the "reload the disk fresh,
/// relocate the entry by StartBlock+TypeByte" pattern used by <see cref="AkaiDiskWriter"/>.
/// </summary>
public static class AkaiSampleAudioLoader
{
    public static SampleAudioResult Load(SampleAudioLoadRequest request)
    {
        AkaiVolume volume;
        byte[] image;
        DiskDensity density;

        try
        {
            var (linearImage, dens, warnings, _, _) = DiskImageLoader.LoadLinearImage(request.SourcePath);
            image = linearImage;
            density = dens;
            volume = AkaiFloppyReader.ReadFloppyVolume(image, density, warnings);
        }
        catch (Exception ex)
        {
            return Fail(SampleAudioLoadFailureReason.IoError, $"Could not re-read the source disk: {ex.Message}");
        }

        var entry = volume.Files.FirstOrDefault(f =>
            f.StartBlock == request.StartBlock && f.TypeByte == request.TypeByte && f.Kind == AkaiFileKind.Sample);
        if (entry is null)
        {
            return Fail(SampleAudioLoadFailureReason.FileNotFound,
                "This sample could no longer be found on the source disk. Try rescanning.");
        }

        // Only S1000 is verified: the 150-byte-header-then-raw-PCM layout was confirmed
        // empirically against real S1000 samples. S900 header parsing is already flagged
        // elsewhere in this app as unverified, and S3000 uses a longer header layout.
        if (entry.Platform != AkaiPlatform.S1000)
        {
            return Fail(SampleAudioLoadFailureReason.UnsupportedPlatform,
                $"Audio preview is only supported for S1000 samples right now (this is {entry.Platform}).");
        }

        var (primaryAudio, primaryInfo) = ReadOne(image, density, entry);
        if (primaryAudio is null || primaryInfo is null)
        {
            return Fail(SampleAudioLoadFailureReason.IoError, "Could not decode this sample's header.");
        }

        string? partnerName = FindStereoPartnerName(entry.Name);
        short[]? partnerAudio = null;
        if (partnerName is not null)
        {
            var partnerEntry = volume.Files.FirstOrDefault(f =>
                f.Kind == AkaiFileKind.Sample && f.Platform == entry.Platform &&
                string.Equals(f.Name, partnerName, StringComparison.Ordinal));
            if (partnerEntry is not null)
            {
                var (audio, _) = ReadOne(image, density, partnerEntry);
                partnerAudio = audio;
            }
            else
            {
                partnerName = null; // no partner actually present on this disk - stay mono
            }
        }

        bool isStereo = partnerAudio is not null;
        bool primaryIsLeft = !entry.Name.EndsWith("-R", StringComparison.Ordinal);
        short[] left = isStereo ? (primaryIsLeft ? primaryAudio : partnerAudio!) : primaryAudio;
        short[]? right = isStereo ? (primaryIsLeft ? partnerAudio! : primaryAudio) : null;

        return new SampleAudioResult(
            Success: true,
            FailureReason: SampleAudioLoadFailureReason.None,
            ErrorDetail: null,
            SampleRateHz: primaryInfo.SampleRateHz,
            Left: left,
            Right: right,
            IsStereo: isStereo,
            PrimaryName: entry.Name,
            PartnerName: partnerName,
            Loops: primaryInfo.Loops,
            PlaybackMode: primaryInfo.PlaybackMode);
    }

    /// <summary>
    /// Recognizes a trailing "-L"/"-R" stereo-pair suffix (the convention used across this
    /// user's real AKAI library) and returns the complementary sibling's expected name, or
    /// null if the name doesn't use this convention at all.
    /// </summary>
    internal static string? FindStereoPartnerName(string name)
    {
        if (name.Length < 3 || name[^2] != '-') return null;
        char last = name[^1];
        if (last != 'L' && last != 'R') return null;
        char other = last == 'L' ? 'R' : 'L';
        return name[..^1] + other;
    }

    private static (short[]? Audio, AkaiSampleInfo? Info) ReadOne(byte[] image, DiskDensity density, AkaiFileEntry entry)
    {
        byte[] full;
        try
        {
            full = AkaiFloppyReader.ReadFileData(image, density, entry.StartBlock, entry.SizeBytes);
        }
        catch
        {
            return (null, null);
        }

        if (full.Length < AkaiSampleParser.HeaderSize) return (null, null);

        var info = AkaiSampleParser.Parse(full, out _);
        if (info is null) return (null, null);

        int dataLength = full.Length - AkaiSampleParser.HeaderSize;
        int sampleCount = dataLength / 2; // signed 16-bit mono PCM, little-endian
        var samples = new short[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            int off = AkaiSampleParser.HeaderSize + i * 2;
            samples[i] = (short)(full[off] | (full[off + 1] << 8));
        }

        return (samples, info);
    }

    private static SampleAudioResult Fail(SampleAudioLoadFailureReason reason, string detail) =>
        new(false, reason, detail, 0, Array.Empty<short>(), null, false, "", null,
            Array.Empty<AkaiLoopInfo>(), null);
}
