using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MehSql.App.Services;
using MehSql.Core.Connections;
using MehSql.Core.Execution;
using MehSql.Core.Export;
using MehSql.Core.Querying;
using MehSql.Core.Schema;
using ReactiveUI;
using Serilog;

namespace MehSql.App.ViewModels;

/// <summary>
/// Main window view model managing the query editor, execution, and results display.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IConnectionFactory _connectionFactory;
    private readonly SettingsService _settingsService;
    private CancellationTokenSource? _cancellationTokenSource;
    private bool _isSynchronizingTabSelection;

    /// <summary>
    /// Exposes the settings service for window position persistence.
    /// </summary>
    public SettingsService SettingsService => _settingsService;

    public MainWindowViewModel(IConnectionFactory connectionFactory)
        : this(connectionFactory, new SettingsService())
    {
    }

    public MainWindowViewModel(IConnectionFactory connectionFactory, SettingsService settingsService)
    {
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
        Log.Logger.Information("Initializing MainWindowViewModel with connection factory");

        RunQueryCommand = ReactiveCommand.CreateFromTask<string?>(RunQueryAsync);
        CancelQueryCommand = ReactiveCommand.Create(CancelQuery);
        NewQueryTabCommand = ReactiveCommand.Create(AddNewQueryTab);
        CloseQueryTabCommand = ReactiveCommand.Create<QueryTabViewModel?>(CloseQueryTab);
        MoveTabLeftCommand = ReactiveCommand.Create<QueryTabViewModel?>(MoveQueryTabLeft);
        MoveTabRightCommand = ReactiveCommand.Create<QueryTabViewModel?>(MoveQueryTabRight);
        ReRunHistoryQueryCommand = ReactiveCommand.CreateFromTask<QueryHistoryItemViewModel?>(ReRunHistoryQueryAsync);
        OpenHistoryInNewTabCommand = ReactiveCommand.Create<QueryHistoryItemViewModel?>(OpenHistoryInNewTab);

        var queryPager = CreateQueryPager(connectionFactory);
        var explainService = new ExplainService(connectionFactory);
        var exportService = new ExportService();
        Results = new ResultsViewModel(queryPager, explainService, exportService);
        SchemaExplorer = new SchemaExplorerViewModel(new SchemaService(connectionFactory), GenerateSelectTopRowsSql, HandleSchemaAction);

        RecentFiles = new ObservableCollection<string>(_settingsService.Settings.RecentFiles);
        RecentSqlFiles = new ObservableCollection<string>(_settingsService.Settings.RecentSqlFiles);

        var initialTab = AddQueryTab("SELECT 1 AS id, 'Hello' AS message;", "Query 1", filePath: null, select: true);
        initialTab.MarkClean();

        Log.Logger.Information("MainWindowViewModel initialization complete");
    }

    #region Properties

    private string _sqlText = string.Empty;
    public string SqlText
    {
        get => _sqlText;
        set
        {
            var normalized = value ?? string.Empty;
            if (string.Equals(_sqlText, normalized, StringComparison.Ordinal))
            {
                return;
            }
            this.RaiseAndSetIfChanged(ref _sqlText, normalized);

            if (_isSynchronizingTabSelection)
            {
                return;
            }

            if (SelectedQueryTab is null)
            {
                return;
            }

            if (!string.Equals(SelectedQueryTab.SqlText, normalized, StringComparison.Ordinal))
            {
                _isSynchronizingTabSelection = true;
                SelectedQueryTab.SetText(normalized, markDirty: true);
                _isSynchronizingTabSelection = false;
            }
        }
    }

    private bool _isExecuting;
    public bool IsExecuting
    {
        get => _isExecuting;
        private set => this.RaiseAndSetIfChanged(ref _isExecuting, value);
    }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    private bool _hasError;
    public bool HasError
    {
        get => _hasError;
        private set => this.RaiseAndSetIfChanged(ref _hasError, value);
    }

    public ResultsViewModel Results { get; }
    public SchemaExplorerViewModel SchemaExplorer { get; private set; }

    private string? _currentDatabasePath;
    public string? CurrentDatabasePath
    {
        get => _currentDatabasePath;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentDatabasePath, value);
            this.RaisePropertyChanged(nameof(WindowTitle));
        }
    }

    public string WindowTitle => string.IsNullOrEmpty(CurrentDatabasePath)
        ? "MehSQL"
        : $"MehSQL : {CurrentDatabasePath}";

    public ObservableCollection<string> RecentFiles { get; }
    public ObservableCollection<string> RecentSqlFiles { get; }

    public ObservableCollection<QueryTabViewModel> QueryTabs { get; } = [];

    private QueryTabViewModel? _selectedQueryTab;
    public QueryTabViewModel? SelectedQueryTab
    {
        get => _selectedQueryTab;
        set
        {
            if (_selectedQueryTab == value)
            {
                return;
            }

            if (_selectedQueryTab is not null)
            {
                _selectedQueryTab.PropertyChanged -= OnSelectedTabPropertyChanged;
            }

            this.RaiseAndSetIfChanged(ref _selectedQueryTab, value);

            if (_selectedQueryTab is not null)
            {
                _selectedQueryTab.PropertyChanged += OnSelectedTabPropertyChanged;
            }

            _isSynchronizingTabSelection = true;
            SqlText = value?.SqlText ?? string.Empty;
            _isSynchronizingTabSelection = false;

            this.RaisePropertyChanged(nameof(ActiveSqlFilePath));
        }
    }

    public string? ActiveSqlFilePath => SelectedQueryTab?.FilePath;

    public ObservableCollection<QueryHistoryItemViewModel> QueryHistory { get; } = [];

    private string _queryHistorySearchText = string.Empty;
    public string QueryHistorySearchText
    {
        get => _queryHistorySearchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _queryHistorySearchText, value ?? string.Empty);
            RefreshQueryHistory();
        }
    }

    private SqlExecutionTarget _lastExecutionTarget = SqlExecutionTarget.WholeEditor;
    public SqlExecutionTarget LastExecutionTarget
    {
        get => _lastExecutionTarget;
        private set => this.RaiseAndSetIfChanged(ref _lastExecutionTarget, value);
    }

    private string? _schemaActionStatus;
    public string? SchemaActionStatus
    {
        get => _schemaActionStatus;
        private set => this.RaiseAndSetIfChanged(ref _schemaActionStatus, value);
    }

    private string? _pendingFindText;
    public string? PendingFindText
    {
        get => _pendingFindText;
        private set => this.RaiseAndSetIfChanged(ref _pendingFindText, value);
    }

    private AutocompleteCache? _autocompleteCache;
    /// <summary>
    /// Cached schema metadata for SQL autocomplete. Built when a database is opened.
    /// </summary>
    public AutocompleteCache? AutocompleteCache
    {
        get => _autocompleteCache;
        private set => this.RaiseAndSetIfChanged(ref _autocompleteCache, value);
    }

    #endregion

    #region Commands

    public ICommand RunQueryCommand { get; }
    public ICommand CancelQueryCommand { get; }
    public ICommand NewQueryTabCommand { get; }
    public ICommand CloseQueryTabCommand { get; }
    public ICommand MoveTabLeftCommand { get; }
    public ICommand MoveTabRightCommand { get; }
    public ICommand ReRunHistoryQueryCommand { get; }
    public ICommand OpenHistoryInNewTabCommand { get; }

    #endregion

    public async Task ExecutePlannedQueryAsync(SqlExecutionRequest request)
    {
        LastExecutionTarget = request.Target;
        await RunQueryAsync(request.Sql);
    }

    private async Task RunQueryAsync(string? sqlOverride)
    {
        var sqlToRun = sqlOverride ?? SqlText;

        Log.Logger.Debug("RunQueryAsync called with SQL: {SqlText}", sqlToRun?.Substring(0, Math.Min(sqlToRun.Length, 100)));

        if (string.IsNullOrWhiteSpace(sqlToRun))
        {
            Log.Logger.Warning("RunQueryAsync called with empty SQL text");
            ErrorMessage = "Please enter a SQL query.";
            HasError = true;
            return;
        }

        ErrorMessage = null;
        HasError = false;

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;

        IsExecuting = true;
        Results.Sql = sqlToRun;

        try
        {
            Log.Logger.Information("Executing query: {SqlText}", sqlToRun[..Math.Min(sqlToRun.Length, 200)]);
            await Results.RunAsync(ct);
            TrackQueryHistory(sqlToRun);
            Log.Logger.Information("Query executed successfully");
        }
        catch (OperationCanceledException)
        {
            Log.Logger.Information("Query was cancelled by user");
            ErrorMessage = "Query was cancelled.";
            HasError = true;
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Error executing query: {ErrorMessage}", ex.Message);
            ErrorMessage = $"Error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsExecuting = false;
            Log.Logger.Debug("RunQueryAsync completed, IsExecuting set to false");
        }
    }

    private void CancelQuery()
    {
        _cancellationTokenSource?.Cancel();
    }

    public QueryTabViewModel AddNewQueryTab()
    {
        return AddQueryTab(string.Empty, BuildNextTabTitle(), filePath: null, select: true);
    }

    public QueryTabViewModel AddQueryTab(string sqlText, string title, string? filePath, bool select)
    {
        var tab = new QueryTabViewModel(title, sqlText, filePath);
        QueryTabs.Add(tab);

        if (select || SelectedQueryTab is null)
        {
            SelectedQueryTab = tab;
        }

        return tab;
    }

    public void CloseQueryTab(QueryTabViewModel? tab)
    {
        tab ??= SelectedQueryTab;
        if (tab is null)
        {
            return;
        }

        var index = QueryTabs.IndexOf(tab);
        if (index < 0)
        {
            return;
        }

        QueryTabs.RemoveAt(index);

        if (QueryTabs.Count == 0)
        {
            var newTab = AddQueryTab(string.Empty, BuildNextTabTitle(), filePath: null, select: true);
            newTab.MarkClean();
            return;
        }

        var newIndex = Math.Min(index, QueryTabs.Count - 1);
        SelectedQueryTab = QueryTabs[newIndex];
    }

    public void MoveQueryTabLeft(QueryTabViewModel? tab)
    {
        tab ??= SelectedQueryTab;
        if (tab is null)
        {
            return;
        }

        var index = QueryTabs.IndexOf(tab);
        if (index <= 0)
        {
            return;
        }

        QueryTabs.Move(index, index - 1);
        SelectedQueryTab = tab;
    }

    public void MoveQueryTabRight(QueryTabViewModel? tab)
    {
        tab ??= SelectedQueryTab;
        if (tab is null)
        {
            return;
        }

        var index = QueryTabs.IndexOf(tab);
        if (index < 0 || index >= QueryTabs.Count - 1)
        {
            return;
        }

        QueryTabs.Move(index, index + 1);
        SelectedQueryTab = tab;
    }

    public void RenameQueryTab(QueryTabViewModel? tab, string? title)
    {
        if (tab is null || string.IsNullOrWhiteSpace(title))
        {
            return;
        }

        tab.Title = title.Trim();
    }

    public async Task OpenSqlFileInTabAsync(string filePath, bool openInNewTab = true)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(filePath);
        var sqlText = await File.ReadAllTextAsync(normalizedPath);

        QueryTabViewModel tab;
        if (openInNewTab || SelectedQueryTab is null)
        {
            tab = AddQueryTab(sqlText, Path.GetFileName(normalizedPath), normalizedPath, select: true);
        }
        else
        {
            tab = SelectedQueryTab;
            tab.Title = Path.GetFileName(normalizedPath);
            tab.FilePath = normalizedPath;
            tab.SetText(sqlText, markDirty: false);
            SelectedQueryTab = tab;
        }

        tab.MarkClean();
        TrackRecentSqlFile(normalizedPath);
    }

    public async Task SaveActiveSqlFileAsync(string filePath)
    {
        if (SelectedQueryTab is null || string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        var normalizedPath = Path.GetFullPath(filePath);
        await File.WriteAllTextAsync(normalizedPath, SelectedQueryTab.SqlText);

        SelectedQueryTab.FilePath = normalizedPath;
        SelectedQueryTab.Title = Path.GetFileName(normalizedPath);
        SelectedQueryTab.MarkClean();
        TrackRecentSqlFile(normalizedPath);
        this.RaisePropertyChanged(nameof(ActiveSqlFilePath));
    }

    public async Task OpenDatabaseAsync(string filePath)
    {
        Log.Logger.Information("Opening database file: {FilePath}", filePath);

        if (string.IsNullOrEmpty(filePath))
        {
            Log.Logger.Warning("OpenDatabaseAsync called with null or empty file path");
            return;
        }

        try
        {
            var newFactory = new ConnectionFactory(filePath);
            Log.Logger.Debug("Created new ConnectionFactory for file: {FilePath}", filePath);

            using var conn = newFactory.CreateConnection();
            await conn.OpenAsync();
            Log.Logger.Information("Successfully opened connection to database: {FilePath}", filePath);

            CurrentDatabasePath = filePath;
            Log.Logger.Information("Updated CurrentDatabasePath to: {FilePath}", filePath);

            var queryPager = CreateQueryPager(newFactory);
            var explainService = new ExplainService(newFactory);
            var exportService = new ExportService();
            var schemaService = new SchemaService(newFactory);
            Log.Logger.Debug("Reinitialized view models with new connection factory");

            Results.UpdateServices(queryPager, explainService, exportService);
            Results.Sql = SqlText;
            Log.Logger.Information("Updated ResultsViewModel with new connection");

            SchemaExplorer.UpdateSchemaService(schemaService, GenerateSelectTopRowsSql, HandleSchemaAction);
            await SchemaExplorer.LoadAsync();
            Log.Logger.Information("Schema explorer loaded successfully for database: {FilePath}", filePath);

            try
            {
                var schema = await schemaService.GetSchemaAsync();
                AutocompleteCache = new AutocompleteCache(schema);
                Log.Logger.Information("Autocomplete cache built with {TableCount} tables", AutocompleteCache.GetAllTables().Count);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to build autocomplete cache");
            }

            ErrorMessage = null;
            HasError = false;
            TrackRecentFile(filePath);
            RefreshQueryHistory();
            Log.Logger.Information("Database {FilePath} opened successfully", filePath);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to open database {FilePath}: {ErrorMessage}", filePath, ex.Message);
            ErrorMessage = $"Failed to open database: {ex.Message}";
            HasError = true;
        }
    }

    public async Task CreateDatabaseAsync(string filePath)
    {
        Log.Logger.Information("Creating new database file: {FilePath}", filePath);

        if (string.IsNullOrEmpty(filePath))
        {
            Log.Logger.Warning("CreateDatabaseAsync called with null or empty file path");
            return;
        }

        try
        {
            var newFactory = new ConnectionFactory(filePath);
            Log.Logger.Debug("Created new ConnectionFactory for file: {FilePath}", filePath);

            using var conn = newFactory.CreateConnection();
            await conn.OpenAsync();
            Log.Logger.Information("Successfully opened connection to new database: {FilePath}", filePath);

            CurrentDatabasePath = filePath;
            Log.Logger.Information("Updated CurrentDatabasePath to: {FilePath}", filePath);

            var queryPager = CreateQueryPager(newFactory);
            var explainService = new ExplainService(newFactory);
            var exportService = new ExportService();
            var schemaService = new SchemaService(newFactory);
            Log.Logger.Debug("Reinitialized view models with new connection factory");

            Results.UpdateServices(queryPager, explainService, exportService);
            Results.Sql = SqlText;

            SchemaExplorer.UpdateSchemaService(schemaService, GenerateSelectTopRowsSql, HandleSchemaAction);
            await SchemaExplorer.LoadAsync();
            Log.Logger.Information("Schema explorer loaded successfully for new database: {FilePath}", filePath);

            try
            {
                var schema = await schemaService.GetSchemaAsync();
                AutocompleteCache = new AutocompleteCache(schema);
                Log.Logger.Information("Autocomplete cache built with {TableCount} tables", AutocompleteCache.GetAllTables().Count);
            }
            catch (Exception ex)
            {
                Log.Logger.Warning(ex, "Failed to build autocomplete cache");
            }

            ErrorMessage = null;
            HasError = false;
            TrackRecentFile(filePath);
            RefreshQueryHistory();
            Log.Logger.Information("New database {FilePath} created successfully", filePath);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to create database {FilePath}: {ErrorMessage}", filePath, ex.Message);
            ErrorMessage = $"Failed to create database: {ex.Message}";
            HasError = true;
        }
    }

    public async Task ReRunHistoryQueryAsync(QueryHistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        SqlText = item.Sql;
        await RunQueryAsync(item.Sql);
    }

    public void OpenHistoryInNewTab(QueryHistoryItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        var title = $"History {DateTime.Now:HHmmss}";
        AddQueryTab(item.Sql, title, filePath: null, select: true);
    }

    public IReadOnlyList<(QueryTabViewModel Tab, int MatchCount)> FindReferences(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        return QueryTabs
            .Select(tab => (tab, MatchCount: CountOccurrences(tab.SqlText, symbol)))
            .Where(x => x.MatchCount > 0)
            .ToList();
    }

    private void HandleSchemaAction(SchemaActionRequest request)
    {
        switch (request.Action)
        {
            case SchemaActionNames.NewQueryTab:
                AddNewQueryTab();
                SchemaActionStatus = "Opened a new query tab.";
                break;
            case SchemaActionNames.RefreshSchema:
                _ = SchemaExplorer.LoadAsync();
                SchemaActionStatus = "Refreshing schema...";
                break;
            case SchemaActionNames.ShowProperties:
                ShowProperties(request);
                break;
            case SchemaActionNames.ViewDdl:
                OpenDdlScript(request);
                break;
            case SchemaActionNames.GenerateCrud:
                OpenCrudTemplates(request);
                break;
            case SchemaActionNames.DropObject:
                OpenDropStatement(request);
                break;
            case SchemaActionNames.FindReferences:
                JumpToReferences(request.Name);
                break;
        }
    }

    private void ShowProperties(SchemaActionRequest request)
    {
        var lines = new List<string>
        {
            $"-- Properties for {request.NodeType}: {request.Name}"
        };

        switch (request.Model)
        {
            case SchemaRootNode root:
            {
                var tableCount = root.Tables.Count;
                var indexCount = root.Tables.Sum(t => t.Indexes.Count);
                var triggerCount = root.Tables.Sum(t => t.Triggers.Count) + root.Views.Sum(v => v.Triggers.Count);
                long? fileSize = null;
                if (!string.IsNullOrWhiteSpace(CurrentDatabasePath) && File.Exists(CurrentDatabasePath))
                {
                    fileSize = new FileInfo(CurrentDatabasePath).Length;
                }

                lines.Add($"Path: {CurrentDatabasePath ?? "(not open)"}");
                if (fileSize.HasValue)
                {
                    lines.Add($"File Size: {fileSize.Value:N0} bytes");
                }

                lines.Add($"Tables: {tableCount}");
                lines.Add($"Indexes: {indexCount}");
                lines.Add($"Triggers: {triggerCount}");
                lines.Add($"Views: {root.Views.Count}");
                break;
            }
            case TableNode table:
                lines.Add($"Schema: {table.Schema}");
                lines.Add($"Columns: {table.Columns.Count}");
                lines.Add($"Foreign Keys: {table.ForeignKeys.Count}");
                lines.Add($"Indexes: {table.Indexes.Count}");
                lines.Add($"Triggers: {table.Triggers.Count}");
                break;
            case ViewNode view:
                lines.Add($"Schema: {view.Schema}");
                lines.Add($"Columns: {view.Columns.Count}");
                lines.Add($"Triggers: {view.Triggers.Count}");
                break;
            case ColumnNode column:
                lines.Add($"Data Type: {column.DataType}");
                lines.Add($"Nullable: {column.IsNullable}");
                lines.Add($"Primary Key: {column.IsPrimaryKey}");
                if (!string.IsNullOrWhiteSpace(column.DefaultValue))
                {
                    lines.Add($"Default: {column.DefaultValue}");
                }

                break;
            case IndexNode index:
                lines.Add($"Unique: {index.IsUnique}");
                lines.Add($"Columns: {string.Join(", ", index.Columns)}");
                break;
            case ForeignKeyNode fk:
                lines.Add($"Column: {fk.ColumnName}");
                lines.Add($"References: {fk.ReferencedTable}({fk.ReferencedColumn})");
                break;
            case TriggerNode trigger:
                lines.Add($"Timing: {trigger.Timing}");
                lines.Add($"Event: {trigger.Event}");
                lines.Add($"Parent: {trigger.ParentObjectName}");
                break;
            default:
                lines.Add("Best-effort properties are unavailable for this object.");
                break;
        }

        var script = string.Join(Environment.NewLine, lines);
        AddQueryTab(script, $"{request.Name} Properties", null, select: true).MarkClean();
        SchemaActionStatus = $"Opened properties for {request.Name}.";
    }

    private void OpenDdlScript(SchemaActionRequest request)
    {
        string script = request.Model switch
        {
            TableNode table => SchemaScriptBuilder.BuildTableDdl(table),
            ViewNode view => SchemaScriptBuilder.BuildViewDdl(view),
            TriggerNode trigger => SchemaScriptBuilder.BuildTriggerDdl(trigger),
            IndexNode index => $"-- Index DDL metadata{Environment.NewLine}-- Name: {index.Name}{Environment.NewLine}-- Columns: {string.Join(", ", index.Columns)}",
            _ => $"-- DDL is not available for {request.NodeType} {request.Name}"
        };

        AddQueryTab(script, $"{request.Name} DDL", null, select: true).MarkClean();
        SchemaActionStatus = $"Opened DDL for {request.Name}.";
    }

    private void OpenCrudTemplates(SchemaActionRequest request)
    {
        if (request.Model is not TableNode table)
        {
            return;
        }

        var script = SchemaScriptBuilder.BuildCrudTemplates(table);
        AddQueryTab(script, $"{table.Name} CRUD", null, select: true).MarkClean();
        SchemaActionStatus = $"Opened CRUD templates for {table.Name}.";
    }

    private void OpenDropStatement(SchemaActionRequest request)
    {
        var statement = SchemaScriptBuilder.BuildDropStatement(request.NodeType, request.Name);
        var script = $"-- Review before running.{Environment.NewLine}{statement}";
        AddQueryTab(script, $"Drop {request.Name}", null, select: true).MarkClean();
        SchemaActionStatus = $"Prepared DROP statement for {request.Name}.";
    }

    private void JumpToReferences(string symbol)
    {
        var matches = FindReferences(symbol);
        if (matches.Count == 0)
        {
            SchemaActionStatus = $"No references found for {symbol} in open query tabs.";
            return;
        }

        var first = matches[0];
        SelectedQueryTab = first.Tab;
        SchemaActionStatus = $"Found {matches.Sum(x => x.MatchCount)} references across {matches.Count} tabs for {symbol}.";
        PendingFindText = symbol;
    }

    public void ClearPendingFindText()
    {
        PendingFindText = null;
    }

    private static int CountOccurrences(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private void OnSelectedTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not QueryTabViewModel tab || tab != SelectedQueryTab)
        {
            return;
        }

        if (e.PropertyName == nameof(QueryTabViewModel.SqlText) && !_isSynchronizingTabSelection)
        {
            _isSynchronizingTabSelection = true;
            SqlText = tab.SqlText;
            _isSynchronizingTabSelection = false;
        }

        if (e.PropertyName == nameof(QueryTabViewModel.FilePath))
        {
            this.RaisePropertyChanged(nameof(ActiveSqlFilePath));
        }
    }

    private void TrackRecentFile(string filePath)
    {
        _settingsService.AddRecentFile(filePath);
        RefreshRecentCollection(RecentFiles, _settingsService.Settings.RecentFiles);
    }

    private void TrackRecentSqlFile(string filePath)
    {
        _settingsService.AddRecentSqlFile(filePath);
        RefreshRecentCollection(RecentSqlFiles, _settingsService.Settings.RecentSqlFiles);
    }

    private void TrackQueryHistory(string sql)
    {
        if (string.IsNullOrWhiteSpace(CurrentDatabasePath))
        {
            return;
        }

        _settingsService.AddQueryHistory(CurrentDatabasePath, sql);
        RefreshQueryHistory();
    }

    private void RefreshQueryHistory()
    {
        QueryHistory.Clear();

        foreach (var entry in _settingsService.GetQueryHistory(CurrentDatabasePath, QueryHistorySearchText))
        {
            QueryHistory.Add(new QueryHistoryItemViewModel
            {
                Sql = entry.Sql,
                ExecutedAtUtc = entry.ExecutedAtUtc
            });
        }
    }

    private static void RefreshRecentCollection(ObservableCollection<string> target, IReadOnlyCollection<string> source)
    {
        target.Clear();
        foreach (var file in source)
        {
            target.Add(file);
        }
    }

    private string BuildNextTabTitle()
    {
        var existing = QueryTabs
            .Select(t => t.Title)
            .Where(t => t.StartsWith("Query ", StringComparison.OrdinalIgnoreCase))
            .Select(t => t[6..])
            .Select(s => int.TryParse(s, out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"Query {existing + 1}";
    }

    private void GenerateSelectTopRowsSql(string tableName)
    {
        Log.Logger.Information("Generating SELECT TOP 1000 query for table: {TableName}", tableName);
        SqlText = $"SELECT * FROM \"{tableName}\" LIMIT 100;";
        Log.Logger.Information("Populated SQL Editor with SELECT TOP 100 query for table: {TableName}", tableName);
    }

    private static IQueryPager CreateQueryPager(IConnectionFactory connectionFactory)
    {
        var options = new QueryOptions();
        return new CachedQueryPager(new QueryPager(connectionFactory), options.MaxCachedPages);
    }
}
