using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AkaiDiskCatalog.App.ViewModels;

namespace AkaiDiskCatalog.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void BrowseFolder_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is not { } storage) return;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder containing .hfe / .img disk images",
            AllowMultiple = false,
        });

        var folder = folders.FirstOrDefault();
        if (folder?.TryGetLocalPath() is { } path && DataContext is MainViewModel vm)
        {
            vm.FolderPath = path;
            if (vm.ScanFolderCommand.CanExecute(null))
                vm.ScanFolderCommand.Execute(null);
        }
    }
}
