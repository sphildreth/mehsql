using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MehSql.App.ViewModels;

namespace MehSql.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnExportCsvClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to CSV",
            DefaultExtension = "csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV Files") { Patterns = ["*.csv"] }
            ]
        });

        if (file != null)
        {
            await vm.Results.ExportToCsvAsync(file.Path.LocalPath, CancellationToken.None);
        }
    }

    private async void OnExportJsonClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export to JSON",
            DefaultExtension = "json",
            FileTypeChoices =
            [
                new FilePickerFileType("JSON Files") { Patterns = ["*.json"] }
            ]
        });

        if (file != null)
        {
            await vm.Results.ExportToJsonAsync(file.Path.LocalPath, CancellationToken.None);
        }
    }
}
