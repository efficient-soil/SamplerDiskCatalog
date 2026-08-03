using System.Windows.Input;
using AkaiDiskCatalog.Data.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AkaiDiskCatalog.App.Models;

public sealed partial class FileRowViewModel : ObservableObject
{
    public FileRowViewModel(FileSearchResult r, ICommand toggleFavoriteCommand)
    {
        Source = r;
        _isFavorite = r.IsFavorite;
        ToggleFavoriteCommand = toggleFavoriteCommand;
    }

    public FileSearchResult Source { get; }
    public ICommand ToggleFavoriteCommand { get; }

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

    public bool CanFavorite => Kind == "Program";

    [ObservableProperty] private bool _isFavorite;
}
