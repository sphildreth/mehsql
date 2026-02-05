using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using MehSql.Core.Querying;

namespace MehSql.App.ViewModels;

/// <summary>
/// Loads result pages incrementally. Do NOT bind full 100k+ results at once.
/// </summary>
public sealed class ResultsViewModel : ViewModelBase
{
    private readonly IQueryPager _pager;

    public ResultsViewModel(IQueryPager pager) => _pager = pager;

    public ObservableCollection<IReadOnlyDictionary<string, object?>> Rows { get; } = new();

    private IReadOnlyList<ColumnInfo> _columns = Array.Empty<ColumnInfo>();
    public IReadOnlyList<ColumnInfo> Columns { get => _columns; private set { _columns = value; RaisePropertyChanged(); } }

    private QueryPageToken? _nextToken;

    private QueryTimings? _timings;
    public QueryTimings? Timings { get => _timings; private set { _timings = value; RaisePropertyChanged(); } }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { _isBusy = value; RaisePropertyChanged(); } }

    public string Sql { get; set; } = "SELECT 1;";

    public async Task RunAsync(CancellationToken ct)
    {
        IsBusy = true;
        try
        {
            Rows.Clear();
            var page = await _pager.ExecuteFirstPageAsync(Sql, new QueryOptions(), ct);
            Apply(page);
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
            Apply(page);
        }
        finally { IsBusy = false; }
    }

    private void Apply(QueryPage page)
    {
        Columns = page.Columns;
        Timings = page.Timings;
        _nextToken = page.NextToken;
        foreach (var r in page.Rows) Rows.Add(r);
    }
}
