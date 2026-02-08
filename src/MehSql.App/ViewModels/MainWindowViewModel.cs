using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MehSql.Core.Connections;
using MehSql.Core.Querying;
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
        Results = new ResultsViewModel(new QueryPager(connectionFactory));
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
}
