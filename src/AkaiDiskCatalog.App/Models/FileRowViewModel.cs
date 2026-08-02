using AkaiDiskCatalog.Data.Models;

namespace AkaiDiskCatalog.App.Models;

public sealed class FileRowViewModel
{
    public FileRowViewModel(FileSearchResult r)
    {
        Source = r;
    }

    public FileSearchResult Source { get; }

    public string DiskFileName => Source.DiskFileName;
    public string VolumeName => Source.VolumeName;
    public string Name => Source.Name;
    public string Kind => Source.Kind;
    public string Platform => Source.Platform;
    public string SizeDisplay => Source.SizeBytes >= 1024
        ? $"{Source.SizeBytes / 1024.0:F1} KB"
        : $"{Source.SizeBytes} B";

    public string SampleRateDisplay => Source.SampleRateHz is { } r ? $"{r:N0} Hz" : "";
    public string DurationDisplay => Source.DurationMs is { } d
        ? (d >= 1000 ? $"{d / 1000.0:F2} s" : $"{d:F0} ms")
        : "";
    public string LoopDisplay => Source.PlaybackMode switch
    {
        null => "",
        "NOLOOP" => "no loop",
        _ => "loop"
    };
    public bool HasWarning => !string.IsNullOrEmpty(Source.ParseWarning);
}
