using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkaiDiskCatalog.App.Models;
using AkaiDiskCatalog.Core.Filesystem;
using AkaiDiskCatalog.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AkaiDiskCatalog.App.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly CatalogRepository _repo;
    private readonly ScanService _scanner;
    private CancellationTokenSource? _scanCts;

    public MainViewModel() : this(DesignTimeRepo()) { }

    public MainViewModel(CatalogRepository repo)
    {
        _repo = repo;
        _scanner = new ScanService(repo);
        RefreshDiskFilters();
        RunSearch();
    }

    private static CatalogRepository DesignTimeRepo()
    {
        var conn = CatalogDatabase.OpenAndInitialize(":memory:");
        return new CatalogRepository(conn);
    }

    [ObservableProperty] private string _folderPath = "";
    [ObservableProperty] private string _statusText = "Choose a folder to scan for .hfe / .img AKAI disk images.";
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginRenameNameCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRenameNameCommand))]
    private bool _isScanning;
    [ObservableProperty] private double _scanProgressFraction;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedKindFilter = "All";
    [ObservableProperty] private string _selectedDiskFilter = "All disks";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRenameSelectedFile))]
    [NotifyCanExecuteChangedFor(nameof(BeginRenameNameCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRenameNameCommand))]
    private FileRowViewModel? _selectedFile;
    [ObservableProperty] private SelectedFileDetail? _detail;

    [ObservableProperty] private bool _isEditingName;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRenameNameCommand))]
    private string _editNameText = "";
    [ObservableProperty] private string? _renameMessage;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginRenameNameCommand))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmRenameNameCommand))]
    private bool _isRenaming;

    public bool CanRenameSelectedFile =>
        SelectedFile != null &&
        (SelectedFile.Kind == "Sample" || SelectedFile.Kind == "Program") &&
        SelectedFile.Platform != "S900";

    public ObservableCollection<string> KindFilters { get; } = new(new[]
    {
        "All", "Favorites", "Sample", "Program", "Drum", "Effects", "QuickLook", "TakeList",
        "Multi", "System", "CdSetup", "OverallSettings900", "Fixup900", "MemoryImage900"
    });

    public ObservableCollection<string> DiskFilters { get; } = new(new[] { "All disks" });

    public ObservableCollection<FileRowViewModel> Results { get; } = new();

    public ObservableCollection<DiskRowInfo> Disks { get; } = new();

    partial void OnSearchTextChanged(string value) => RunSearch();
    partial void OnSelectedKindFilterChanged(string value) => RunSearch();
    partial void OnSelectedDiskFilterChanged(string value) => RunSearch();

    partial void OnSelectedFileChanged(FileRowViewModel? value)
    {
        Detail = value != null ? new SelectedFileDetail(value.Source) : null;
        IsEditingName = false;
        RenameMessage = null;
    }

    private bool CanBeginRenameName() => !IsScanning && !IsRenaming && CanRenameSelectedFile;

    [RelayCommand(CanExecute = nameof(CanBeginRenameName))]
    private void BeginRenameName()
    {
        if (SelectedFile is null) return;
        EditNameText = SelectedFile.Name;
        RenameMessage = null;
        IsEditingName = true;
    }

    [RelayCommand]
    private void CancelRenameName()
    {
        IsEditingName = false;
        RenameMessage = null;
    }

    private bool CanConfirmRenameName() =>
        !IsRenaming && !IsScanning && SelectedFile != null && !string.IsNullOrWhiteSpace(EditNameText);

    [RelayCommand(CanExecute = nameof(CanConfirmRenameName))]
    private async Task ConfirmRenameNameAsync()
    {
        if (SelectedFile is null) return;
        var src = SelectedFile.Source;
        var newName = EditNameText;

        IsRenaming = true;
        RenameMessage = null;
        try
        {
            var request = new RenameRequest(src.DiskSourcePath, src.StartBlock, src.TypeByte, newName);
            var result = await Task.Run(() => AkaiDiskWriter.RenameFile(request));

            if (!result.Success)
            {
                RenameMessage = DescribeFailure(result);
                return;
            }

            _scanner.ScanFile(result.NewImagePath!);
            RefreshDiskFilters();
            RunSearch();
            IsEditingName = false;

            string refNote = result.PatchedReferenceCount > 0
                ? $" Updated {result.PatchedReferenceCount} program reference(s) to the sample."
                : "";
            StatusText = $"Renamed \"{src.Name}\" - saved as a new disk image ({Path.GetFileName(result.NewImagePath)}); the original file was not modified.{refNote}";
        }
        catch (Exception ex)
        {
            RenameMessage = $"Rename failed: {ex.Message}";
        }
        finally
        {
            IsRenaming = false;
        }
    }

    private static string DescribeFailure(RenameResult r) => r.FailureReason switch
    {
        RenameFailureReason.InvalidName => r.ErrorDetail ?? "Invalid name.",
        RenameFailureReason.UnsupportedFile => r.ErrorDetail ?? "This file type can't be renamed.",
        RenameFailureReason.UnsafeCrossReference => r.ErrorDetail ?? "Renaming this sample isn't safe on this disk.",
        RenameFailureReason.FileNotFound => r.ErrorDetail ?? "File not found. Try rescanning.",
        RenameFailureReason.IoError => $"Couldn't write the new disk image: {r.ErrorDetail}",
        _ => r.ErrorDetail ?? "Rename failed.",
    };

    [RelayCommand]
    private void ToggleFavorite(FileRowViewModel? row)
    {
        if (row is null || !row.CanFavorite) return;
        row.IsFavorite = !row.IsFavorite;
        _repo.SetFavorite(row.Source.DiskSourcePath, row.Source.Name, row.Source.Kind, row.IsFavorite);

        if (SelectedKindFilter == "Favorites" && !row.IsFavorite)
            Results.Remove(row);
    }

    [RelayCommand]
    private async Task ScanFolderAsync()
    {
        if (string.IsNullOrWhiteSpace(FolderPath) || !Directory.Exists(FolderPath))
        {
            StatusText = "Please choose a valid folder first.";
            return;
        }

        _scanCts?.Cancel();
        var cts = new CancellationTokenSource();
        _scanCts = cts;

        IsScanning = true;
        ScanProgressFraction = 0;
        StatusText = "Scanning...";

        var progress = new Progress<ScanProgress>(p =>
        {
            ScanProgressFraction = p.Total > 0 ? (double)p.Current / p.Total : 0;
            StatusText = $"{(p.FromCache ? "Cached" : "Decoding")} [{p.Current}/{p.Total}]: {p.CurrentFile}";
        });

        try
        {
            await Task.Run(() => _scanner.ScanFolder(FolderPath, progress, cts.Token), cts.Token);
            RefreshDiskFilters();
            RunSearch();
            var disks = _repo.GetDiskSummaries();
            int problems = disks.Count(d => !d.DecodeOk || d.MissingSectors > 0);
            StatusText = problems == 0
                ? $"Scan complete: {disks.Count} disk(s), {Results.Count} file(s) indexed."
                : $"Scan complete: {disks.Count} disk(s) ({problems} with decode warnings - see Disks panel).";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            StatusText = $"Scan failed: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    private void RefreshDiskFilters()
    {
        Disks.Clear();
        DiskFilters.Clear();
        DiskFilters.Add("All disks");
        foreach (var d in _repo.GetDiskSummaries())
        {
            Disks.Add(new DiskRowInfo(d));
            DiskFilters.Add(d.SourcePath);
        }
    }

    private void RunSearch()
    {
        Results.Clear();
        foreach (var r in _repo.Search(SearchText, SelectedKindFilter, SelectedDiskFilter))
            Results.Add(new FileRowViewModel(r, ToggleFavoriteCommand));
    }
}
