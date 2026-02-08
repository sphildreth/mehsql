using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MehSql.Core.Connections;
using MehSql.Core.Execution;
using MehSql.Core.Export;
using MehSql.Core.Querying;
using MehSql.Core.Schema;
using ReactiveUI;

namespace MehSql.App.ViewModels;

/// <summary>
/// Main window view model managing the query editor, execution, and results display.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IConnectionFactory _connectionFactory;
    private CancellationTokenSource? _cancellationTokenSource;

    public MainWindowViewModel(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;

        // Initialize commands
        RunQueryCommand = ReactiveCommand.CreateFromTask(RunQueryAsync);
        CancelQueryCommand = ReactiveCommand.Create(CancelQuery);

        // Initialize child view models
        var queryPager = new QueryPager(connectionFactory);
        var explainService = new ExplainService(connectionFactory);
        var exportService = new ExportService();
        Results = new ResultsViewModel(queryPager, explainService, exportService);
        SchemaExplorer = new SchemaExplorerViewModel(new SchemaService(connectionFactory));

        // Load schema on startup
        _ = SchemaExplorer.LoadAsync();
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
    public SchemaExplorerViewModel SchemaExplorer { get; }

    private string? _currentDatabasePath;
    public string? CurrentDatabasePath
    {
        get => _currentDatabasePath;
        private set => this.RaiseAndSetIfChanged(ref _currentDatabasePath, value);
    }

    #endregion

    #region Commands

    public ICommand RunQueryCommand { get; }
    public ICommand CancelQueryCommand { get; }

    #endregion

    private async Task RunQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(SqlText))
        {
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
            await Results.RunAsync(ct);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "Query was cancelled.";
            HasError = true;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
            HasError = true;
        }
        finally
        {
            IsExecuting = false;
        }
    }

    private void CancelQuery()
    {
        _cancellationTokenSource?.Cancel();
    }

    public async Task OpenDatabaseAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        try
        {
            // Create a new connection factory for the selected file
            var newFactory = new ConnectionFactory(filePath);
            using var conn = newFactory.CreateConnection();
            await conn.OpenAsync();

            // Update current database path
            CurrentDatabasePath = filePath;

            // Reinitialize view models with new connection
            var queryPager = new QueryPager(newFactory);
            var explainService = new ExplainService(newFactory);
            var exportService = new ExportService();

            // Update Results view model
            Results.Sql = SqlText;

            // Reload schema explorer
            await SchemaExplorer.LoadAsync();

            ErrorMessage = null;
            HasError = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to open database: {ex.Message}";
            HasError = true;
        }
    }

    public async Task CreateDatabaseAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        try
        {
            // DecentDB will create the file if it doesn't exist
            var newFactory = new ConnectionFactory(filePath);
            using var conn = newFactory.CreateConnection();
            await conn.OpenAsync();

            // Update current database path
            CurrentDatabasePath = filePath;

            // Reinitialize view models with new connection
            var queryPager = new QueryPager(newFactory);
            var explainService = new ExplainService(newFactory);
            var exportService = new ExportService();

            // Reload schema explorer to show empty database
            await SchemaExplorer.LoadAsync();

            ErrorMessage = null;
            HasError = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to create database: {ex.Message}";
            HasError = true;
        }
    }
}
