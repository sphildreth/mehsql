using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MehSql.Core.Import;

namespace MehSql.App.Views;

public partial class ImportOptionsDialog : Window
{
    private AnalysisResult? _analysis;

    public ImportOptions? Result { get; private set; }

    public ImportOptionsDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initialize the dialog with the selected SQLite path and analysis result.
    /// </summary>
    public void Initialize(string sqlitePath, AnalysisResult analysis)
    {
        _analysis = analysis;
        SourcePathText.Text = sqlitePath;

        // Default destination: same directory, same name with .ddb extension
        var dir = Path.GetDirectoryName(sqlitePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(sqlitePath) + ".ddb";
        DestPathText.Text = Path.Combine(dir, name);

        // Preview
        var tableCount = analysis.Tables.Count;
        var totalRows = analysis.TotalRows;
        var skippedCount = analysis.SkippedIndexes.Count;
        var preview = $"{tableCount} tables, {totalRows:N0} total rows";
        if (skippedCount > 0)
            preview += $", {skippedCount} indexes will be skipped";
        if (analysis.Warnings.Count > 0)
            preview += $", {analysis.Warnings.Count} warnings";
        PreviewText.Text = preview;

        ImportButton.IsEnabled = true;
    }

    private async void OnBrowseClick(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save DecentDB File",
            DefaultExtension = "ddb",
            SuggestedFileName = Path.GetFileNameWithoutExtension(SourcePathText.Text ?? "database") + ".ddb",
            FileTypeChoices =
            [
                new FilePickerFileType("DecentDB Database") { Patterns = ["*.ddb"] }
            ]
        });

        if (file is not null)
        {
            DestPathText.Text = file.Path.LocalPath;
        }
    }

    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        var destPath = DestPathText.Text?.Trim();
        if (string.IsNullOrEmpty(destPath))
            return;

        Result = new ImportOptions
        {
            SqlitePath = SourcePathText.Text ?? "",
            DecentDbPath = destPath,
            LowercaseIdentifiers = LowercaseCheck.IsChecked == true,
            CommitBatchSize = (int)(BatchSizeInput.Value ?? 5000),
            Overwrite = true
        };

        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Result = null;
        Close();
    }
}
