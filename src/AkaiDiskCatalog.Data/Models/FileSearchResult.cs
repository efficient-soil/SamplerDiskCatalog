using System;

namespace AkaiDiskCatalog.Data.Models;

public sealed class FileSearchResult
{
    public long FileId { get; set; }
    public string DiskFileName { get; set; } = "";
    public string DiskSourcePath { get; set; } = "";
    public string VolumeName { get; set; } = "";
    public string Platform { get; set; } = "";
    public string OsVersion { get; set; } = "";

    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public int SizeBytes { get; set; }
    public int StartBlock { get; set; }
    public string? ParseWarning { get; set; }
    public bool HasWarning => !string.IsNullOrEmpty(ParseWarning);

    public int? SampleRateHz { get; set; }
    public double? DurationMs { get; set; }
    public int? RootKey { get; set; }
    public int? CentsTune { get; set; }
    public int? SemitoneTune { get; set; }
    public string? PlaybackMode { get; set; }
    public int? NumLoops { get; set; }

    public int? MidiChannel { get; set; }
    public int? KeyLow { get; set; }
    public int? KeyHigh { get; set; }
    public int? NumKeygroups { get; set; }

    public string? DetailsJson { get; set; }
}

public sealed class DiskSummary
{
    public long DiskId { get; set; }
    public string FileName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string Density { get; set; } = "";
    public bool DecodeOk { get; set; }
    public int MissingSectors { get; set; }
    public int TotalSectors { get; set; }
    public string VolumeName { get; set; } = "";
    public int FileCount { get; set; }
    public DateTime ScannedAtUtc { get; set; }
}
