using System;
using System.IO;
using System.Linq;
using AkaiDiskCatalog.Core.Filesystem.Models;

namespace AkaiDiskCatalog.Core.Filesystem;

public enum RenameFailureReason
{
    None,
    FileNotFound,
    InvalidName,
    UnsupportedFile,
    UnsafeCrossReference,
    IoError,
}

public sealed record RenameRequest(string SourcePath, int StartBlock, byte TypeByte, string NewName);

public sealed record RenameResult(
    bool Success,
    RenameFailureReason FailureReason,
    string? ErrorDetail,
    string? NewImagePath,
    int PatchedReferenceCount);

/// <summary>
/// Writes changes into a copy of an AKAI disk image. The source file passed in a
/// <see cref="RenameRequest"/> is never modified - the result is always written to a new
/// .img file (converting from .hfe if needed, via <see cref="DiskImageLoader.LoadLinearImage"/>
/// which already assembles a decoded .hfe into the same linear byte layout as a real .img).
/// </summary>
public static class AkaiDiskWriter
{
    public static RenameResult RenameFile(RenameRequest request)
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
            return Fail(RenameFailureReason.IoError, $"Could not re-read the source disk: {ex.Message}");
        }

        var entry = volume.Files.FirstOrDefault(f => f.StartBlock == request.StartBlock && f.TypeByte == request.TypeByte);
        if (entry is null)
        {
            return Fail(RenameFailureReason.FileNotFound,
                "This file could no longer be found on the source disk (it may have changed since it was last scanned). Try rescanning.");
        }

        if (entry.Platform == AkaiPlatform.S900)
        {
            return Fail(RenameFailureReason.UnsupportedFile, "Renaming S900 files is not supported.");
        }
        if (entry.Kind != AkaiFileKind.Sample && entry.Kind != AkaiFileKind.Program)
        {
            return Fail(RenameFailureReason.UnsupportedFile, $"Renaming a {entry.Kind} file is not supported (only Sample and Program).");
        }

        bool diskHasS3000Program = volume.Files.Any(f => f.Kind == AkaiFileKind.Program && f.Platform == AkaiPlatform.S3000);
        if (entry.Kind == AkaiFileKind.Sample && diskHasS3000Program)
        {
            return Fail(RenameFailureReason.UnsafeCrossReference,
                "This disk contains one or more S3000 programs. S3000 program keygroups aren't parsed by this app, so sample references inside them can't be located and patched safely. Renaming this sample is blocked to avoid leaving the disk inconsistent.");
        }

        byte[] encodedName;
        try
        {
            encodedName = AkaiCharset.Encode1000(request.NewName);
        }
        catch (AkaiNameEncodeException ex)
        {
            return Fail(RenameFailureReason.InvalidName, ex.Message);
        }

        // Snapshot the raw old bytes before mutating - used as the exact-match needle for
        // cross-reference patching. Byte-exact comparison avoids ambiguity from Decode1000
        // mapping unrecognized codes to '.', which could cause false/missed matches if we
        // instead compared decoded display strings.
        byte[] oldNameBytes = image.AsSpan(entry.DirectoryEntryOffset, 12).ToArray();

        Array.Copy(encodedName, 0, image, entry.DirectoryEntryOffset, 12);
        int ramNameOffset = entry.StartBlock * 1024 + 3;
        Array.Copy(encodedName, 0, image, ramNameOffset, 12);

        int patched = 0;
        if (entry.Kind == AkaiFileKind.Sample)
        {
            foreach (var prog in volume.Files.Where(f => f.Kind == AkaiFileKind.Program && f.StartBlock != entry.StartBlock))
            {
                patched += PatchSampleReferencesInProgram(image, density, prog, oldNameBytes, encodedName);
            }
        }

        string outPath;
        try
        {
            outPath = BuildOutputPath(request.SourcePath);
            File.WriteAllBytes(outPath, image);
        }
        catch (Exception ex)
        {
            return Fail(RenameFailureReason.IoError, ex.Message);
        }

        return new RenameResult(true, RenameFailureReason.None, null, outPath, patched);
    }

    private static int PatchSampleReferencesInProgram(byte[] image, DiskDensity density, AkaiFileEntry prog, byte[] oldNameBytes, byte[] newNameBytes)
    {
        if (prog.Platform == AkaiPlatform.S3000) return 0; // defensive - caller already blocks this case

        byte[] full = AkaiFloppyReader.ReadFileData(image, density, prog.StartBlock, prog.SizeBytes);
        if (full.Length < AkaiProgramParser.HeaderSize) return 0;

        int numKeygroups = full[42];
        int patched = 0;
        int off = AkaiProgramParser.HeaderSize;

        for (int k = 0; k < numKeygroups; k++)
        {
            if (off + AkaiProgramParser.KeygroupSize > full.Length) break;

            int vzBase = off + AkaiProgramParser.VelZonesBaseOffset;
            for (int v = 0; v < AkaiProgramParser.VelZonesPerKg; v++)
            {
                int vOff = vzBase + v * AkaiProgramParser.VelZoneSize;
                if (vOff + 12 > full.Length) continue;

                if (full.AsSpan(vOff, 12).SequenceEqual(oldNameBytes))
                {
                    if (AkaiFloppyReader.WriteFileBytes(image, density, prog.StartBlock, prog.SizeBytes, vOff, newNameBytes))
                        patched++;
                }
            }

            off += AkaiProgramParser.KeygroupSize;
        }

        return patched;
    }

    internal static string BuildOutputPath(string sourcePath)
    {
        string dir = Path.GetDirectoryName(sourcePath) ?? "";
        string baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "disk";

        string candidate = Path.Combine(dir, baseName + ".img");
        if (!File.Exists(candidate)) return candidate;

        for (int i = 1; i < 1000; i++)
        {
            candidate = Path.Combine(dir, $"{baseName} ({i}).img");
            if (!File.Exists(candidate)) return candidate;
        }

        return Path.Combine(dir, $"{baseName} ({Guid.NewGuid():N}).img");
    }

    private static RenameResult Fail(RenameFailureReason reason, string detail) =>
        new(false, reason, detail, null, 0);
}
