using AkaiDiskCatalog.Data.Models;

namespace AkaiDiskCatalog.App.Models;

public sealed class DiskRowInfo
{
    public DiskRowInfo(DiskSummary d)
    {
        Source = d;
    }

    public DiskSummary Source { get; }

    public string FileName => Source.FileName;
    public string VolumeName => Source.VolumeName;
    public int FileCount => Source.FileCount;
    public string Density => Source.Density switch
    {
        "HighDensity1_6M" => "HD 1.6MB",
        "LowDensity800K" => "LD 800KB",
        _ => Source.Density
    };
    public string StatusText => !Source.DecodeOk
        ? "decode failed"
        : Source.MissingSectors > 0
            ? $"{Source.MissingSectors}/{Source.TotalSectors} sectors unreadable"
            : "OK";
    public bool HasProblem => !Source.DecodeOk || Source.MissingSectors > 0;
}
