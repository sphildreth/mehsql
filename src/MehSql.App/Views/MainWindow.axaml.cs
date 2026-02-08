using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MehSql.App.Services;
using MehSql.App.ViewModels;

namespace MehSql.App.Views;

public partial class MainWindow : Window
{
    private readonly ThemeManager _themeManager = new ThemeManager();

    public MainWindow()
    {
        InitializeComponent();

        // Enable drag and drop
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);

        // Rebuild DataGrid columns when the ViewModel's Columns property changes
        DataContextChanged += (_, _) =>
        {
            Serilog.Log.Logger.Debug("MainWindow.DataContextChanged fired, DataContext type: {Type}", DataContext?.GetType().Name ?? "null");
            if (DataContext is MainWindowViewModel vm)
            {
                Serilog.Log.Logger.Debug("Subscribed to Results.PropertyChanged");
                vm.Results.PropertyChanged += (_, args) =>
                {
                    Serilog.Log.Logger.Debug("Results.PropertyChanged fired: {PropertyName}", args.PropertyName);
                    if (args.PropertyName == nameof(vm.Results.Columns))
                    {
                        Serilog.Log.Logger.Debug("Columns changed, rebuilding DataGrid columns. Column count: {Count}", vm.Results.Columns.Count);
                        RebuildDataGridColumns(vm.Results.Columns, vm.Results.Rows);
                    }
                };
            }
        };
    }

    private void RebuildDataGridColumns(
        System.Collections.Generic.IReadOnlyList<Core.Querying.ColumnInfo> columns,
        System.Collections.ObjectModel.ObservableCollection<Dictionary<string, object?>> rows)
    {
        Serilog.Log.Logger.Debug("RebuildDataGridColumns called with {Count} columns, {RowCount} rows", columns.Count, rows.Count);

        // Detach ItemsSource while rebuilding columns
        ResultsDataGrid.ItemsSource = null;
        ResultsDataGrid.Columns.Clear();

        foreach (var col in columns)
        {
            var key = col.Name;
            ResultsDataGrid.Columns.Add(new DataGridTextColumn
            {
                Header = key,
                Binding = new Binding($"[{key}]"),
                IsReadOnly = true
            });
        }

        // Re-attach ItemsSource after columns are built
        ResultsDataGrid.ItemsSource = rows;

        Serilog.Log.Logger.Debug("DataGrid rebuilt: {ColCount} columns, {RowCount} rows bound",
            ResultsDataGrid.Columns.Count, rows.Count);
    }

    private async void OnOpenDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Database",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("DecentDB Database") { Patterns = ["*.ddb"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            await vm.OpenDatabaseAsync(files[0].Path.LocalPath);
        }
    }

    private async void OnNewDatabaseClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Create New Database",
            DefaultExtension = "ddb",
            FileTypeChoices =
            [
                new FilePickerFileType("DecentDB Database") { Patterns = ["*.ddb"] }
            ]
        });

        if (file != null)
        {
            await vm.CreateDatabaseAsync(file.Path.LocalPath);
        }
    }

    private void OnToggleThemeClick(object? sender, RoutedEventArgs e)
    {
        _themeManager.ToggleTheme();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        // Check if the drag contains files
        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files != null && files.Any(f => f.Name.EndsWith(".ddb", StringComparison.OrdinalIgnoreCase)))
            {
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;

        if (e.DataTransfer.Contains(DataFormat.File))
        {
            var files = e.DataTransfer.TryGetFiles();
            if (files != null)
            {
                var ddbFile = files.FirstOrDefault(f => f.Name.EndsWith(".ddb", StringComparison.OrdinalIgnoreCase));
                if (ddbFile != null)
                {
                    await vm.OpenDatabaseAsync(ddbFile.Path.LocalPath);
                }
            }
        }

        e.Handled = true;
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
