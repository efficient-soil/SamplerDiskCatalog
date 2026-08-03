using System.Reflection;
using Avalonia.Controls;

namespace AkaiDiskCatalog.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = version != null ? $"Version {version.Major}.{version.Minor}.{version.Build}" : "";
    }
}
