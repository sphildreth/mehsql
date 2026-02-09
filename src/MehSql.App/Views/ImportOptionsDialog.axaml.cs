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
    private GenericAnalysisResult? _genericAnalysis;

    public ImportOptions? Result { get; private set; }
    public GenericImportOptions? GenericResult { get; private set; }

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
        FormatText.Text = ImportFormatDetector.FormatDisplayName(ImportFormat.SQLite);
        HeaderText.Text = "Import SQLite Database";

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

    /// <summary>
    /// Initialize the dialog with a generic analysis result (any import format).
    /// </summary>
    public void Initialize(string sourcePath, GenericAnalysisResult analysis)
    {
        _genericAnalysis = analysis;
        SourcePathText.Text = sourcePath;
        FormatText.Text = ImportFormatDetector.FormatDisplayName(analysis.Format);
        HeaderText.Text = $"Import {ImportFormatDetector.FormatDisplayName(analysis.Format)}";

        // Default destination
        var dir = Path.GetDirectoryName(sourcePath) ?? ".";
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        // Strip double extensions like .sql.gz → .sql → base name
        while (Path.GetExtension(name) is { Length: > 0 })
            name = Path.GetFileNameWithoutExtension(name);
        DestPathText.Text = Path.Combine(dir, name + ".ddb");

        // Preview
        var preview = $"{analysis.TableNames.Count} tables, {analysis.TotalRows:N0} total rows";
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

        if (_genericAnalysis is not null)
        {
            GenericResult = new GenericImportOptions
            {
                SourcePath = SourcePathText.Text ?? "",
                DecentDbPath = destPath,
                Format = _genericAnalysis.Format,
                LowercaseIdentifiers = LowercaseCheck.IsChecked == true,
                CommitBatchSize = (int)(BatchSizeInput.Value ?? 5000),
                Overwrite = true
            };
        }
        else
        {
            Result = new ImportOptions
            {
                SourcePath = SourcePathText.Text ?? "",
                DecentDbPath = destPath,
                LowercaseIdentifiers = LowercaseCheck.IsChecked == true,
                CommitBatchSize = (int)(BatchSizeInput.Value ?? 5000),
                Overwrite = true
            };
        }

        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Result = null;
        GenericResult = null;
        Close();
    }
}
