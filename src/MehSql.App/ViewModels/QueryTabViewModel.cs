using System;
using ReactiveUI;

namespace MehSql.App.ViewModels;

public sealed class QueryTabViewModel : ViewModelBase
{
    private string _title;
    private string _sqlText;
    private string? _filePath;
    private bool _isDirty;

    public QueryTabViewModel(string title, string sqlText = "", string? filePath = null)
    {
        Id = Guid.NewGuid();
        _title = title;
        _sqlText = sqlText;
        _filePath = filePath;
        _isDirty = false;
    }

    public Guid Id { get; }

    public string Title
    {
        get => _title;
        set
        {
            this.RaiseAndSetIfChanged(ref _title, value);
            this.RaisePropertyChanged(nameof(Header));
        }
    }

    public string Header => IsDirty ? $"{Title} *" : Title;

    public string SqlText
    {
        get => _sqlText;
        set => this.RaiseAndSetIfChanged(ref _sqlText, value);
    }

    public string? FilePath
    {
        get => _filePath;
        set => this.RaiseAndSetIfChanged(ref _filePath, value);
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isDirty, value);
            this.RaisePropertyChanged(nameof(Header));
        }
    }

    public void MarkDirty()
    {
        IsDirty = true;
    }

    public void MarkClean()
    {
        IsDirty = false;
    }

    public void SetText(string sql, bool markDirty)
    {
        SqlText = sql;
        if (markDirty)
        {
            MarkDirty();
        }
        else
        {
            MarkClean();
        }
    }
}

public sealed class QueryHistoryItemViewModel : ViewModelBase
{
    public string Sql { get; init; } = string.Empty;
    public DateTimeOffset ExecutedAtUtc { get; init; }
    public string DisplayTime => ExecutedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string Preview => Sql.Length <= 120 ? Sql : $"{Sql[..117]}...";
}
