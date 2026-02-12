using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DecentDB.AdoNet;
using MehSql.Core.Connections;
using MehSql.Core.Querying;
using Serilog;

namespace MehSql.Core.Execution;

/// <summary>
/// Executes SQL queries with timing capture and streaming support.
/// </summary>
public interface IQueryExecutor
{
    /// <summary>
    /// Executes a SQL query and streams the results.
    /// </summary>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="options">Query options including page size.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of result rows.</returns>
    IAsyncEnumerable<ResultRow> ExecuteQueryAsync(
        string sql,
        QueryOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a SQL query and returns a single page of results with timing information.
    /// </summary>
    /// <param name="sql">The SQL query to execute.</param>
    /// <param name="options">Query options including page size.</param>
    /// <param name="offset">Optional offset for pagination.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Query result page with timing information.</returns>
    Task<QueryResult> ExecutePageAsync(
        string sql,
        QueryOptions options,
        int? offset = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a single row in the query result.
/// </summary>
public sealed record ResultRow(IReadOnlyDictionary<string, object?> Values);

/// <summary>
/// Result of executing a query page with timing information.
/// </summary>
public sealed record QueryResult(
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<ResultRow> Rows,
    QueryTimings Timings,
    int TotalRowCount,
    bool DefaultLimitApplied);

/// <summary>
/// Default implementation of IQueryExecutor using DecentDB.
/// Uses raw SQL execution through DecentDB.AdoNet for maximum flexibility.
/// </summary>
public sealed class QueryExecutor : IQueryExecutor
{
    private readonly IConnectionFactory _connectionFactory;

    public QueryExecutor(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        Log.Logger.Information("QueryExecutor initialized with connection factory");
    }

    public async IAsyncEnumerable<ResultRow> ExecuteQueryAsync(
        string sql,
        QueryOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Log.Logger.Debug("ExecuteQueryAsync called with SQL: {SqlText}", sql?.Substring(0, Math.Min(sql.Length, 100)));
        
        if (string.IsNullOrWhiteSpace(sql))
        {
            Log.Logger.Error("ExecuteQueryAsync called with empty SQL query");
            throw new ArgumentException("SQL query cannot be empty.", nameof(sql));
        }

        using var connection = _connectionFactory.CreateConnection();
        Log.Logger.Debug("Opening connection for query execution");
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        Log.Logger.Debug("Created command with SQL: {SqlText}", sql?.Substring(0, Math.Min(sql.Length, 200)));

        Log.Logger.Debug("Executing command and getting reader");
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // Get column information
        var columnCount = reader.FieldCount;
        Log.Logger.Debug("Query returned {ColumnCount} columns", columnCount);
        var columnNames = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            columnNames[i] = reader.GetName(i);
        }

        Log.Logger.Debug("Starting to stream rows");
        // Stream rows
        var rowCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, object?>(columnCount);
            for (int i = 0; i < columnCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                values[columnNames[i]] = value;
            }
            rowCount++;
            yield return new ResultRow(values);
        }
        Log.Logger.Debug("Streamed {RowCount} rows", rowCount);
    }

    public async Task<QueryResult> ExecutePageAsync(
        string sql,
        QueryOptions options,
        int? offset = null,
        CancellationToken cancellationToken = default)
    {
        Log.Logger.Debug("ExecutePageAsync called with SQL: {SqlText}, Offset: {Offset}, PageSize: {PageSize}", 
            sql?.Substring(0, Math.Min(sql.Length, 100)), offset, options.PageSize);
        
        if (string.IsNullOrWhiteSpace(sql))
        {
            Log.Logger.Error("ExecutePageAsync called with empty SQL query");
            throw new ArgumentException("SQL query cannot be empty.", nameof(sql));
        }

        var dbStopwatch = Stopwatch.StartNew();
        var fetchStopwatch = new Stopwatch();

        using var connection = _connectionFactory.CreateConnection();
        Log.Logger.Debug("Opening connection for page query execution");
        await connection.OpenAsync(cancellationToken);

        // Normalize a trailing terminator so appended paging clauses remain valid SQL.
        var sqlForPaging = TrimTrailingTerminator(sql);
        var paginatedSql = sqlForPaging;
        var defaultLimitApplied = false;
        if (offset.HasValue)
        {
            paginatedSql = $"{sqlForPaging} LIMIT {options.PageSize} OFFSET {offset.Value}";
            Log.Logger.Debug("Applied pagination with OFFSET: {Offset}, LIMIT: {Limit}", offset.Value, options.PageSize);
        }
        else if (options.ApplyDefaultLimit && !ContainsLimitClause(sqlForPaging))
        {
            // Add limit for first page if not already present
            paginatedSql = $"{sqlForPaging} LIMIT {options.PageSize}";
            defaultLimitApplied = true;
            Log.Logger.Debug("Applied initial LIMIT: {Limit} for first page", options.PageSize);
        }

        using var command = connection.CreateCommand();
        command.CommandText = paginatedSql;
        Log.Logger.Debug("Created command with paginated SQL: {SqlText}", paginatedSql?.Substring(0, Math.Min(paginatedSql.Length, 200)));

        Log.Logger.Debug("Executing command and getting reader");
        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        dbStopwatch.Stop();
        fetchStopwatch.Start();

        // Get column information
        var columns = new List<ColumnInfo>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            var type = reader.GetFieldType(i);
            Log.Logger.Debug("Processing column {Index}: {Name} of type {Type}", i, name, type?.Name ?? "unknown");
            columns.Add(new ColumnInfo(name, type?.Name ?? "unknown"));
        }

        // Read rows
        var rows = new List<ResultRow>();
        var rowCount = 0;
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                values[reader.GetName(i)] = value;
            }
            rows.Add(new ResultRow(values));
            rowCount++;
        }
        Log.Logger.Debug("Read {RowCount} rows from result set", rowCount);

        fetchStopwatch.Stop();

        // Get total count - skip if query already has LIMIT/OFFSET
        var totalCount = sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase)
            ? rows.Count
            : await GetTotalCountAsync(sql, connection, cancellationToken);

        var timings = new QueryTimings(
            DbExecutionTime: dbStopwatch.Elapsed,
            FetchTime: fetchStopwatch.Elapsed,
            UiBindTime: null);

        Log.Logger.Information("Query execution completed: {RowCount} rows, DB: {DbTime}ms, Fetch: {FetchTime}ms", 
            rows.Count, dbStopwatch.ElapsedMilliseconds, fetchStopwatch.ElapsedMilliseconds);

        return new QueryResult(columns, rows, timings, totalCount, defaultLimitApplied);
    }

    private static async Task<int> GetTotalCountAsync(string sql, DecentDBConnection connection, CancellationToken ct)
    {
        // DecentDB doesn't support subqueries in COUNT, so we return -1 to indicate unknown
        // In a production implementation, you might parse the SQL to extract the FROM clause
        // or use a different approach (e.g., separate count query with same WHERE clause)
        return -1;
    }

    private static bool ContainsLimitClause(string sql)
    {
        return sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimTrailingTerminator(string sql)
    {
        var trimmed = sql.TrimEnd();
        return trimmed.EndsWith(';')
            ? trimmed[..^1].TrimEnd()
            : trimmed;
    }
}
