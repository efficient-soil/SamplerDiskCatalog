using System.ComponentModel;
using AkaiDiskCatalog.App.ViewModels;
using Avalonia.Controls;

namespace AkaiDiskCatalog.App.Views;

public partial class LoopEditorWindow : Window
{
    public LoopEditorWindow()
    {
        InitializeComponent();
    }

    public LoopEditorWindow(LoopEditorViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            viewModel.Dispose();
        };
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoopEditorViewModel.NewImagePath) &&
            DataContext is LoopEditorViewModel { NewImagePath: not null })
        {
            Close();
        }
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close();
}
