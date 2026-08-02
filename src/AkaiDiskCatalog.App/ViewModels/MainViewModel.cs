using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AkaiDiskCatalog.App.Models;
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
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanProgressFraction;

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedKindFilter = "All";
    [ObservableProperty] private string _selectedDiskFilter = "All disks";

    [ObservableProperty] private FileRowViewModel? _selectedFile;
    [ObservableProperty] private SelectedFileDetail? _detail;

    public ObservableCollection<string> KindFilters { get; } = new(new[]
    {
        "All", "Sample", "Program", "Drum", "Effects", "QuickLook", "TakeList",
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
            Results.Add(new FileRowViewModel(r));
    }
}
