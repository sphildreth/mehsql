using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MehSql.Core.Execution;
using MehSql.Core.Export;
using MehSql.Core.Querying;
using ReactiveUI;
using Serilog;

namespace MehSql.App.ViewModels;

/// <summary>
/// Loads result pages incrementally. Do NOT bind full 100k+ results at once.
/// </summary>
public sealed class ResultsViewModel : ViewModelBase
{
    private IQueryPager _pager;
    private IExplainService _explainService;
    private IExportService _exportService;

    public ResultsViewModel(IQueryPager pager, IExplainService explainService, IExportService exportService)
    {
        _pager = pager;
        _explainService = explainService;
        _exportService = exportService;

        ExplainQueryCommand = ReactiveCommand.CreateFromTask(ExplainQueryAsync);
        ExplainAnalyzeCommand = ReactiveCommand.CreateFromTask(ExplainAnalyzeAsync);
        ExportToCsvCommand = ReactiveCommand.CreateFromTask<string>(ExportToCsvAsync);
        ExportToJsonCommand = ReactiveCommand.CreateFromTask<string>(ExportToJsonAsync);
    }
    
    public void UpdateServices(IQueryPager pager, IExplainService explainService, IExportService exportService)
    {
        _pager = pager;
        _explainService = explainService;
        _exportService = exportService;
    }

    public ObservableCollection<Dictionary<string, object?>> Rows { get; } = new();

    private IReadOnlyList<ColumnInfo> _columns = Array.Empty<ColumnInfo>();
    public IReadOnlyList<ColumnInfo> Columns
    {
        get => _columns;
        private set => this.RaiseAndSetIfChanged(ref _columns, value);
    }

    private QueryPageToken? _nextToken;

    private QueryTimings? _timings;
    public QueryTimings? Timings
    {
        get => _timings;
        private set => this.RaiseAndSetIfChanged(ref _timings, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    private bool _hasOrderingWarning;
    public bool HasOrderingWarning
    {
        get => _hasOrderingWarning;
        private set => this.RaiseAndSetIfChanged(ref _hasOrderingWarning, value);
    }

    private QueryExecutionPlan? _executionPlan;
    public QueryExecutionPlan? ExecutionPlan
    {
        get => _executionPlan;
        private set => this.RaiseAndSetIfChanged(ref _executionPlan, value);
    }

    private bool _showExecutionPlan;
    public bool ShowExecutionPlan
    {
        get => _showExecutionPlan;
        set => this.RaiseAndSetIfChanged(ref _showExecutionPlan, value);
    }

    private string? _executionPlanError;
    public string? ExecutionPlanError
    {
        get => _executionPlanError;
        private set => this.RaiseAndSetIfChanged(ref _executionPlanError, value);
    }

    public string Sql { get; set; } = "SELECT 1;";

    private string? _exportStatus;
    public string? ExportStatus
    {
        get => _exportStatus;
        private set => this.RaiseAndSetIfChanged(ref _exportStatus, value);
    }

    private bool _isExporting;
    public bool IsExporting
    {
        get => _isExporting;
        private set => this.RaiseAndSetIfChanged(ref _isExporting, value);
    }

    public ICommand ExplainQueryCommand { get; }
    public ICommand ExplainAnalyzeCommand { get; }
    public ICommand ExportToCsvCommand { get; }
    public ICommand ExportToJsonCommand { get; }

    private static bool DetectOrdering(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        var normalized = sql.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
        return normalized.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        Log.Logger.Debug("ResultsViewModel.RunAsync called with SQL: {SqlText}", Sql?.Substring(0, Math.Min(Sql?.Length ?? 0, 100)) ?? "");
        IsBusy = true;
        try
        {
            Rows.Clear();
            Log.Logger.Debug("Cleared existing rows");
            var page = await _pager.ExecuteFirstPageAsync(Sql ?? "", new QueryOptions(), ct);
            Log.Logger.Debug("Received page with {RowCount} rows and {ColumnCount} columns", page.Rows.Count, page.Columns.Count);
            
            Apply(page, isFirstPage: true);
            Log.Logger.Debug("Applied page data, Rows collection now has {RowCount} items", Rows.Count);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Error in RunAsync: {ErrorMessage}", ex.Message);
            throw;
        }
        finally { IsBusy = false; }
    }

    public async Task LoadMoreAsync(CancellationToken ct)
    {
        if (_nextToken is null) return;
        IsBusy = true;
        try
        {
            var page = await _pager.ExecuteNextPageAsync(Sql, new QueryOptions(), _nextToken, ct);
            Apply(page, isFirstPage: false);
        }
        finally { IsBusy = false; }
    }

    public async Task ExplainQueryAsync(CancellationToken ct)
    {
        IsBusy = true;
        ExecutionPlanError = null;
        try
        {
            ExecutionPlan = await _explainService.ExplainAsync(Sql, ct);
            ShowExecutionPlan = true;
        }
        catch (Exception ex)
        {
            ExecutionPlanError = $"Failed to get execution plan: {ex.Message}";
            ShowExecutionPlan = false;
        }
        finally { IsBusy = false; }
    }

    public async Task ExplainAnalyzeAsync(CancellationToken ct)
    {
        IsBusy = true;
        ExecutionPlanError = null;
        try
        {
            ExecutionPlan = await _explainService.ExplainAnalyzeAsync(Sql, ct);

            // Also run the actual query to get results and timings
            await RunAsync(ct);

            // Show execution plan after getting results
            ShowExecutionPlan = true;
        }
        catch (Exception ex)
        {
            ExecutionPlanError = $"Failed to analyze execution: {ex.Message}";
            ShowExecutionPlan = false;
        }
        finally { IsBusy = false; }
    }

    public async Task ExportToCsvAsync(string filePath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(filePath) || Rows.Count == 0)
        {
            return;
        }

        IsExporting = true;
        ExportStatus = "Exporting to CSV...";

        try
        {
            var pages = GetAllPagesAsync(ct);
            var options = new ExportOptions
            {
                IncludeHeaders = true,
                FormatDatesAsIso = true
            };

            await using var fileStream = File.Create(filePath);
            await _exportService.ExportToCsvAsync(pages, fileStream, options, ct);

            ExportStatus = $"Exported to {Path.GetFileName(filePath)}";
        }
        catch (OperationCanceledException)
        {
            ExportStatus = "Export cancelled";
        }
        catch (Exception ex)
        {
            ExportStatus = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    public async Task ExportToJsonAsync(string filePath, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(filePath) || Rows.Count == 0)
        {
            return;
        }

        IsExporting = true;
        ExportStatus = "Exporting to JSON...";

        try
        {
            var pages = GetAllPagesAsync(ct);
            var options = new ExportOptions
            {
                IncludeHeaders = false,
                FormatDatesAsIso = true
            };

            await using var fileStream = File.Create(filePath);
            await _exportService.ExportToJsonAsync(pages, fileStream, options, ct);

            ExportStatus = $"Exported to {Path.GetFileName(filePath)}";
        }
        catch (OperationCanceledException)
        {
            ExportStatus = "Export cancelled";
        }
        catch (Exception ex)
        {
            ExportStatus = $"Export failed: {ex.Message}";
        }
        finally
        {
            IsExporting = false;
        }
    }

    private async IAsyncEnumerable<QueryPage> GetAllPagesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        // First page (already loaded)
        var firstPage = new QueryPage(Columns, Rows.ToList(), null, Timings ?? new QueryTimings(null, TimeSpan.Zero, null));
        yield return firstPage;

        // Load remaining pages
        if (_nextToken != null)
        {
            var currentToken = _nextToken;
            while (currentToken != null && !ct.IsCancellationRequested)
            {
                var page = await _pager.ExecuteNextPageAsync(Sql, new QueryOptions(), currentToken, ct);
                if (page.Rows.Count == 0)
                {
                    break;
                }
                yield return page;
                currentToken = page.NextToken;
            }
        }
    }

    private void Apply(QueryPage page, bool isFirstPage)
    {
        Log.Logger.Debug("ResultsViewModel.Apply called with {RowCount} rows, {ColumnCount} columns, isFirstPage: {IsFirstPage}", 
            page.Rows.Count, page.Columns.Count, isFirstPage);
        Timings = page.Timings;
        _nextToken = page.NextToken;
        Log.Logger.Debug("Adding {RowCount} rows to Rows collection", page.Rows.Count);
        foreach (var r in page.Rows) Rows.Add((Dictionary<string, object?>)r);
        
        // Set Columns after Rows so the DataGrid has data when columns are rebuilt
        Columns = page.Columns;
        
        Log.Logger.Debug("Finished adding rows, Rows collection now has {RowCount} items", Rows.Count);
        if (isFirstPage)
        {
            HasOrderingWarning = !DetectOrdering(Sql);
            Log.Logger.Debug("Set HasOrderingWarning to {HasOrderingWarning}", HasOrderingWarning);
        }
    }
}
