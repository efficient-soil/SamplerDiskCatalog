using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AkaiDiskCatalog.App.ViewModels;
using AkaiDiskCatalog.App.Views;
using AkaiDiskCatalog.Data;

namespace AkaiDiskCatalog.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var dbPath = CatalogDatabase.DefaultDatabasePath();
            var conn = CatalogDatabase.OpenAndInitialize(dbPath);
            var repo = new CatalogRepository(conn);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(repo),
            };

            desktop.ShutdownRequested += (_, _) => conn.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
