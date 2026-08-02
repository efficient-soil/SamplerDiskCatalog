using System;
using System.Collections.Generic;
using System.IO;
using AkaiDiskCatalog.Core.Filesystem;
using AkaiDiskCatalog.Core.Filesystem.Models;
using AkaiDiskCatalog.Core.Hfe;

namespace AkaiDiskCatalog.Core;

public static class DiskImageLoader
{
    private const long HighDensityBytes = 1600L * 1024; // 1,638,400
    private const long LowDensityBytes = 800L * 1024;   // 819,200

    /// <summary>
    /// Produces the raw linear block image for a .hfe or .img path - decoding HFE flux to
    /// linear bytes via <see cref="HfeDecoder"/> if needed - without doing any AKAI
    /// filesystem parsing. Shared by <see cref="Load"/> and by the disk writer (rename).
    /// </summary>
    public static (byte[] Image, DiskDensity Density, List<string> Warnings, int TotalSectorsExpected, int MissingSectorCount) LoadLinearImage(string path)
    {
        var warnings = new List<string>();
        byte[] linearImage;
        int totalSectorsExpected = 0;
        int missingSectorCount = 0;
        string ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext == ".hfe")
        {
            var decoded = HfeDecoder.Decode(path);
            totalSectorsExpected = decoded.Cylinders * decoded.Heads * decoded.SectorsPerTrack;
            missingSectorCount = decoded.MissingSectors.Count;
            if (decoded.MissingSectors.Count > 0)
            {
                warnings.Add($"{decoded.MissingSectors.Count} of {totalSectorsExpected} sectors could not be decoded from the flux stream (bad/weak areas or unusual formatting). Missing regions are zero-filled.");
            }
            linearImage = decoded.ToLinearImage();
        }
        else if (ext == ".img")
        {
            linearImage = File.ReadAllBytes(path);
        }
        else
        {
            throw new NotSupportedException($"Unsupported file extension '{ext}'. Expected .hfe or .img.");
        }

        var density = ClassifyDensity(linearImage.Length, warnings);
        return (linearImage, density, warnings, totalSectorsExpected, missingSectorCount);
    }

    public static AkaiDiskImage Load(string path)
    {
        var disk = new AkaiDiskImage
        {
            SourcePath = path,
            SourceFileName = Path.GetFileName(path),
        };

        try
        {
            var (linearImage, density, warnings, totalSectorsExpected, missingSectorCount) = LoadLinearImage(path);
            disk.Density = density;
            disk.TotalSectorsExpected = totalSectorsExpected;
            disk.MissingSectorCount = missingSectorCount;
            disk.Warnings.AddRange(warnings);

            var volume = AkaiFloppyReader.ReadFloppyVolume(linearImage, disk.Density, disk.Warnings);
            disk.Volumes.Add(volume);

            foreach (var file in volume.Files)
            {
                PopulateFileMetadata(linearImage, disk.Density, file, disk.Warnings);
            }
        }
        catch (Exception ex)
        {
            disk.DecodeOk = false;
            disk.Warnings.Add($"Failed to decode disk: {ex.Message}");
        }

        return disk;
    }

    private static DiskDensity ClassifyDensity(long length, System.Collections.Generic.List<string> warnings)
    {
        if (length >= HighDensityBytes) return DiskDensity.HighDensity1_6M;
        if (length >= LowDensityBytes) return DiskDensity.LowDensity800K;
        warnings.Add($"Unrecognized image size ({length} bytes); assuming high-density 1.6MB layout.");
        return DiskDensity.HighDensity1_6M;
    }

    private static void PopulateFileMetadata(byte[] image, DiskDensity density, AkaiFileEntry file, System.Collections.Generic.List<string> diskWarnings)
    {
        try
        {
            switch (file.Kind)
            {
                case AkaiFileKind.Sample:
                {
                    var head = AkaiFloppyReader.ReadFileData(image, density, file.StartBlock, file.SizeBytes, AkaiSampleParser.HeaderSize);
                    file.Sample = AkaiSampleParser.Parse(head, out var w);
                    file.ParseWarning = w;
                    break;
                }
                case AkaiFileKind.Program:
                {
                    var full = AkaiFloppyReader.ReadFileData(image, density, file.StartBlock, file.SizeBytes);
                    file.Program = AkaiProgramParser.Parse(full, file.Platform, out var w);
                    file.ParseWarning = w;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            file.ParseWarning = $"Metadata parse error: {ex.Message}";
        }
    }
}
