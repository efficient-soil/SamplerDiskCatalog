using System;
using System.Collections.Generic;

namespace AkaiDiskCatalog.Core.Filesystem.Models;

public sealed class AkaiDiskImage
{
    public string SourcePath { get; set; } = "";
    public string SourceFileName { get; set; } = "";
    public DiskDensity Density { get; set; }
    public List<AkaiVolume> Volumes { get; } = new();
    public List<string> Warnings { get; } = new();
    public bool DecodeOk { get; set; } = true;
    public int TotalSectorsExpected { get; set; }
    public int MissingSectorCount { get; set; }
}

public enum DiskDensity { Unknown, LowDensity800K, HighDensity1_6M }

public sealed class AkaiVolume
{
    public string Name { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public AkaiPlatform Platform { get; set; }
    public List<AkaiFileEntry> Files { get; } = new();
}

public sealed class AkaiFileEntry
{
    public string Name { get; set; } = "";
    public byte TypeByte { get; set; }
    public AkaiPlatform Platform { get; set; }
    public AkaiFileKind Kind { get; set; }
    public int SizeBytes { get; set; }
    public int StartBlock { get; set; }
    public string OsVersion { get; set; } = "";

    // Populated lazily depending on Kind:
    public AkaiSampleInfo? Sample { get; set; }
    public AkaiProgramInfo? Program { get; set; }
    public string? ParseWarning { get; set; }
}

public sealed class AkaiSampleInfo
{
    public string RamName { get; set; } = "";
    public int SampleRateHz { get; set; }
    public int NumSamples { get; set; }
    public double DurationMs { get; set; }
    public int RootKey { get; set; }
    public int CentsTune { get; set; }
    public int SemitoneTune { get; set; }
    public string PlaybackMode { get; set; } = "";
    public int NumLoops { get; set; }
    public List<AkaiLoopInfo> Loops { get; } = new();
    public bool IsStereoPartner { get; set; }
}

public sealed class AkaiLoopInfo
{
    public int At { get; set; }
    public int LengthSamples { get; set; }
    public int TimeMs { get; set; }
}

public sealed class AkaiProgramInfo
{
    public string RamName { get; set; } = "";
    public int MidiChannel { get; set; } // -1 = omni
    public int KeyLow { get; set; }
    public int KeyHigh { get; set; }
    public int OctaveOffset { get; set; }
    public bool KeygroupCrossfade { get; set; }
    public int NumKeygroups { get; set; }
    public List<AkaiKeygroupInfo> Keygroups { get; } = new();
    /// <summary>True if this is an S3000 program whose keygroup layout differs from S1000
    /// and was not deeply parsed (only the program header is populated).</summary>
    public bool KeygroupsUnparsed { get; set; }
}

public sealed class AkaiKeygroupInfo
{
    public int KeyLow { get; set; }
    public int KeyHigh { get; set; }
    public int CentsTune { get; set; }
    public int SemitoneTune { get; set; }
    public int Filter { get; set; }
    public bool VelocityCrossfade { get; set; }
    public List<AkaiVelocityZoneInfo> VelocityZones { get; } = new();
}

public sealed class AkaiVelocityZoneInfo
{
    public string SampleName { get; set; } = "";
    public int VelocityLow { get; set; }
    public int VelocityHigh { get; set; }
    public int CentsTune { get; set; }
    public int SemitoneTune { get; set; }
    public int Loudness { get; set; }
    public int Filter { get; set; }
    public int Pan { get; set; }
    public string PlaybackMode { get; set; } = "";
}
