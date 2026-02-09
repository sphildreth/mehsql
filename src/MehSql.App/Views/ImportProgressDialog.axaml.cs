using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MehSql.Core.Import;
using Serilog;

namespace MehSql.App.Views;

public partial class ImportProgressDialog : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _stopwatch = new();
    private DispatcherTimer? _elapsedTimer;
    private bool _isComplete;

    public ImportReport? Report { get; private set; }

    public ImportProgressDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Runs the import on a background thread, updating progress on the UI thread.
    /// </summary>
    public async Task RunImportAsync(ImportOptions options)
    {
        _stopwatch.Start();

        // Timer to update elapsed time every second
        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => ElapsedLabel.Text = $"Elapsed: {_stopwatch.Elapsed:m\\:ss}";
        _elapsedTimer.Start();

        var progress = new Progress<ImportProgress>(OnProgressUpdate);
        var service = new SqliteImportService();

        try
        {
            Report = await Task.Run(
                () => service.ImportAsync(options, progress, _cts.Token),
                _cts.Token);

            ShowComplete(Report);
        }
        catch (OperationCanceledException)
        {
            ShowCancelled(options.DecentDbPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Import failed: {Message}", ex.Message);
            ShowFailed(ex);
        }
        finally
        {
            _stopwatch.Stop();
            _elapsedTimer?.Stop();
            ElapsedLabel.Text = $"Elapsed: {_stopwatch.Elapsed:m\\:ss}";
        }
    }

    /// <summary>
    /// Runs the import using a generic import source (PG dump, MySQL dump, etc.).
    /// </summary>
    public async Task RunImportAsync(IImportSource source, GenericImportOptions options)
    {
        _stopwatch.Start();

        _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _elapsedTimer.Tick += (_, _) => ElapsedLabel.Text = $"Elapsed: {_stopwatch.Elapsed:m\\:ss}";
        _elapsedTimer.Start();

        var progress = new Progress<ImportProgress>(OnProgressUpdate);

        try
        {
            Report = await Task.Run(
                () => source.ImportAsync(options, progress, _cts.Token),
                _cts.Token);

            ShowComplete(Report);
        }
        catch (OperationCanceledException)
        {
            ShowCancelled(options.DecentDbPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Import failed: {Message}", ex.Message);
            ShowFailed(ex);
        }
        finally
        {
            _stopwatch.Stop();
            _elapsedTimer?.Stop();
            ElapsedLabel.Text = $"Elapsed: {_stopwatch.Elapsed:m\\:ss}";
        }
    }

    private void OnProgressUpdate(ImportProgress p)
    {
        PhaseLabel.Text = p.Phase switch
        {
            ImportPhase.Analyzing => "📋 Analyzing Schema...",
            ImportPhase.CreatingSchema => "🏗️ Creating Tables...",
            ImportPhase.CopyingData => "📦 Copying Data...",
            ImportPhase.CreatingIndexes => "📇 Creating Indexes...",
            ImportPhase.Complete => "✅ Import Complete",
            ImportPhase.Failed => "❌ Import Failed",
            ImportPhase.Cancelled => "⚠️ Import Cancelled",
            _ => p.Phase.ToString()
        };

        StatusMessage.Text = p.Message;

        // Overall progress (based on tables)
        if (p.TablesTotal > 0)
        {
            OverallLabel.Text = $"Tables: {p.TablesCompleted} / {p.TablesTotal}";
            OverallProgress.Maximum = p.TablesTotal;
            OverallProgress.Value = p.TablesCompleted;
        }

        // Per-table progress (based on rows)
        if (p.Phase == ImportPhase.CopyingData && p.RowsTotal > 0)
        {
            TableLabel.Text = $"{p.CurrentTable}: {p.RowsCompleted:N0} / {p.RowsTotal:N0} rows";
            TableProgress.Maximum = p.RowsTotal;
            TableProgress.Value = p.RowsCompleted;
            TableProgress.IsVisible = true;
        }
        else if (p.Phase == ImportPhase.CreatingIndexes && p.IndexesTotal > 0)
        {
            TableLabel.Text = $"Indexes: {p.IndexesCompleted} / {p.IndexesTotal}";
            TableProgress.Maximum = p.IndexesTotal;
            TableProgress.Value = p.IndexesCompleted;
            TableProgress.IsVisible = true;
        }
        else
        {
            TableLabel.Text = "";
            TableProgress.IsVisible = false;
        }
    }

    private void ShowComplete(ImportReport report)
    {
        _isComplete = true;
        PhaseLabel.Text = "✅ Import Complete";

        var sb = new StringBuilder();
        sb.AppendLine($"Tables: {report.Tables.Count}");
        sb.AppendLine($"Total rows: {report.TotalRows:N0}");
        sb.AppendLine($"Indexes created: {report.IndexesCreated.Count}");
        if (report.UniqueColumnsAdded.Count > 0)
            sb.AppendLine($"Unique columns: {report.UniqueColumnsAdded.Count}");
        if (report.SkippedIndexes.Count > 0)
        {
            sb.AppendLine($"Skipped indexes: {report.SkippedIndexes.Count}");
            foreach (var s in report.SkippedIndexes)
                sb.AppendLine($"  • {s.Table}.{s.Name}: {s.Reason}");
        }
        if (report.Warnings.Count > 0)
        {
            sb.AppendLine($"Warnings: {report.Warnings.Count}");
            foreach (var w in report.Warnings)
                sb.AppendLine($"  • {w}");
        }
        sb.AppendLine($"Elapsed: {report.Elapsed:m\\:ss\\.ff}");

        SummaryText.Text = sb.ToString().TrimEnd();
        SummaryPanel.IsVisible = true;

        CancelButton.IsVisible = false;
        CloseButton.IsVisible = true;
        CopyButton.IsVisible = true;
    }

    private void ShowCancelled(string decentDbPath)
    {
        _isComplete = true;
        PhaseLabel.Text = "⚠️ Import Cancelled";
        StatusMessage.Text = "Import was cancelled by user.";

        // Clean up partial file
        try
        {
            if (System.IO.File.Exists(decentDbPath))
                System.IO.File.Delete(decentDbPath);
            var walPath = decentDbPath + "-wal";
            if (System.IO.File.Exists(walPath))
                System.IO.File.Delete(walPath);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clean up partial import file: {Path}", decentDbPath);
        }

        CancelButton.IsVisible = false;
        CloseButton.IsVisible = true;
    }

    private void ShowFailed(Exception ex)
    {
        _isComplete = true;
        PhaseLabel.Text = "❌ Import Failed";
        StatusMessage.Text = $"Error: {ex.Message}";

        CancelButton.IsVisible = false;
        CloseButton.IsVisible = true;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        CancelButton.IsEnabled = false;
        StatusMessage.Text = "Cancelling...";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is not null && SummaryText.Text is { } text)
        {
            await clipboard.SetTextAsync(text);
            CopyButton.Content = "✅ Copied!";
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // Prevent closing while import is in progress (user must cancel first)
        if (!_isComplete)
        {
            e.Cancel = true;
            return;
        }

        _cts.Dispose();
        _elapsedTimer?.Stop();
        base.OnClosing(e);
    }
}
