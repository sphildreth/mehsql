using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MehSql.Core.Execution;
using MehSql.Core.Querying;
using ReactiveUI;

namespace MehSql.App.ViewModels;

/// <summary>
/// Loads result pages incrementally. Do NOT bind full 100k+ results at once.
/// </summary>
public sealed class ResultsViewModel : ViewModelBase
{
    private readonly IQueryPager _pager;
    private readonly IExplainService _explainService;

    public ResultsViewModel(IQueryPager pager, IExplainService explainService)
    {
        _pager = pager;
        _explainService = explainService;

        ExplainQueryCommand = ReactiveCommand.CreateFromTask(ExplainQueryAsync);
        ExplainAnalyzeCommand = ReactiveCommand.CreateFromTask(ExplainAnalyzeAsync);
    }

    public ObservableCollection<IReadOnlyDictionary<string, object?>> Rows { get; } = new();

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

    public ICommand ExplainQueryCommand { get; }
    public ICommand ExplainAnalyzeCommand { get; }

    private static bool DetectOrdering(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        var normalized = sql.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ");
        return normalized.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            Rows.Clear();
            var page = await _pager.ExecuteFirstPageAsync(Sql, new QueryOptions(), ct);
            Apply(page, isFirstPage: true);
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

    private void Apply(QueryPage page, bool isFirstPage)
    {
        Columns = page.Columns;
        Timings = page.Timings;
        _nextToken = page.NextToken;
        foreach (var r in page.Rows) Rows.Add(r);
        if (isFirstPage)
        {
            HasOrderingWarning = !DetectOrdering(Sql);
        }
    }
}
