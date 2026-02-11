using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        // Initialize commands
        RunQueryCommand = ReactiveCommand.CreateFromTask(RunQueryAsync);
        CancelQueryCommand = ReactiveCommand.Create(CancelQuery);

        // Initialize child view models
        var queryPager = new QueryPager(connectionFactory);
        var explainService = new ExplainService(connectionFactory);
        var exportService = new ExportService();
        Results = new ResultsViewModel(queryPager, explainService, exportService);
        SchemaExplorer = new SchemaExplorerViewModel(new SchemaService(connectionFactory), GenerateSelectTopRowsSql);

        Log.Logger.Information("Initialized child view models");
        
        // Load recent files from settings
        RecentFiles = new ObservableCollection<string>(_settingsService.Settings.RecentFiles);

        Log.Logger.Information("MainWindowViewModel initialization complete");
    }

    #region Properties

    private string _sqlText = "SELECT 1 AS id, 'Hello' AS message;";
    public string SqlText
    {
        get => _sqlText;
        set => this.RaiseAndSetIfChanged(ref _sqlText, value);
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
        private set => this.RaiseAndSetIfChanged(ref _currentDatabasePath, value);
    }

    public ObservableCollection<string> RecentFiles { get; }

    #endregion

    #region Commands

    public ICommand RunQueryCommand { get; }
    public ICommand CancelQueryCommand { get; }

    #endregion

    private async Task RunQueryAsync()
    {
        Log.Logger.Debug("RunQueryAsync called with SQL: {SqlText}", SqlText?.Substring(0, Math.Min(SqlText.Length, 100)));
        
        if (string.IsNullOrWhiteSpace(SqlText))
        {
            Log.Logger.Warning("RunQueryAsync called with empty SQL text");
            ErrorMessage = "Please enter a SQL query.";
            HasError = true;
            return;
        }

        // Clear previous error
        ErrorMessage = null;
        HasError = false;

        // Set up cancellation
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = new CancellationTokenSource();
        var ct = _cancellationTokenSource.Token;

        IsExecuting = true;
        Results.Sql = SqlText;

        try
        {
            Log.Logger.Information("Executing query: {SqlText}", SqlText?.Substring(0, Math.Min(SqlText.Length, 200)));
            await Results.RunAsync(ct);
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
            // Create a new connection factory for the selected file
            var newFactory = new ConnectionFactory(filePath);
            Log.Logger.Debug("Created new ConnectionFactory for file: {FilePath}", filePath);
            
            using var conn = newFactory.CreateConnection();
            await conn.OpenAsync();
            Log.Logger.Information("Successfully opened connection to database: {FilePath}", filePath);

            // Update current database path
            CurrentDatabasePath = filePath;
            Log.Logger.Information("Updated CurrentDatabasePath to: {FilePath}", filePath);

            // Reinitialize view models with new connection
            var queryPager = new QueryPager(newFactory);
            var explainService = new ExplainService(newFactory);
            var exportService = new ExportService();
            var schemaService = new SchemaService(newFactory); // Create new schema service with new connection
            Log.Logger.Debug("Reinitialized view models with new connection factory");

            // Update Results view model with new services
            Results.UpdateServices(queryPager, explainService, exportService);
            Results.Sql = SqlText;
            Log.Logger.Information("Updated ResultsViewModel with new connection");

            // Update the schema explorer with the new schema service and callback
            SchemaExplorer.UpdateSchemaService(schemaService, GenerateSelectTopRowsSql);
            await SchemaExplorer.LoadAsync();
            Log.Logger.Information("Schema explorer loaded successfully for database: {FilePath}", filePath);

            ErrorMessage = null;
            HasError = false;
            TrackRecentFile(filePath);
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
            // DecentDB will create the file if it doesn't exist
            var newFactory = new ConnectionFactory(filePath);
            Log.Logger.Debug("Created new ConnectionFactory for file: {FilePath}", filePath);
            
            using var conn = newFactory.CreateConnection();
            await conn.OpenAsync();
            Log.Logger.Information("Successfully opened connection to new database: {FilePath}", filePath);

            // Update current database path
            CurrentDatabasePath = filePath;
            Log.Logger.Information("Updated CurrentDatabasePath to: {FilePath}", filePath);

            // Reinitialize view models with new connection
            var queryPager = new QueryPager(newFactory);
            var explainService = new ExplainService(newFactory);
            var exportService = new ExportService();
            var schemaService = new SchemaService(newFactory); // Create new schema service with new connection
            Log.Logger.Debug("Reinitialized view models with new connection factory");

            // Update Results view model
            Results.Sql = SqlText;

            // Update the schema explorer with the new schema service and callback
            SchemaExplorer.UpdateSchemaService(schemaService, GenerateSelectTopRowsSql);
            Log.Logger.Information("Created new SchemaExplorerViewModel with updated schema service");
            
            await SchemaExplorer.LoadAsync();
            Log.Logger.Information("Schema explorer loaded successfully for new database: {FilePath}", filePath);

            ErrorMessage = null;
            HasError = false;
            TrackRecentFile(filePath);
            Log.Logger.Information("New database {FilePath} created successfully", filePath);
        }
        catch (Exception ex)
        {
            Log.Logger.Error(ex, "Failed to create database {FilePath}: {ErrorMessage}", filePath, ex.Message);
            ErrorMessage = $"Failed to create database: {ex.Message}";
            HasError = true;
        }
    }

    private void TrackRecentFile(string filePath)
    {
        _settingsService.AddRecentFile(filePath);
        RecentFiles.Clear();
        foreach (var f in _settingsService.Settings.RecentFiles)
        {
            RecentFiles.Add(f);
        }
    }
    
    private void GenerateSelectTopRowsSql(string tableName)
    {
        Log.Logger.Information("Generating SELECT TOP 1000 query for table: {TableName}", tableName);
        var sql = $"SELECT * FROM \"{tableName}\" LIMIT 1000;";
        SqlText = sql;
        Log.Logger.Information("Populated SQL Editor with SELECT TOP 1000 query for table: {TableName}", tableName);
    }
}
