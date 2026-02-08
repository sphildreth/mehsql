using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DecentDB.AdoNet;
using MehSql.Core.Connections;
using MehSql.Core.Querying;

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
    int TotalRowCount);

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
    }

    public async IAsyncEnumerable<ResultRow> ExecuteQueryAsync(
        string sql,
        QueryOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("SQL query cannot be empty.", nameof(sql));
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        // Get column information
        var columnCount = reader.FieldCount;
        var columnNames = new string[columnCount];
        for (int i = 0; i < columnCount; i++)
        {
            columnNames[i] = reader.GetName(i);
        }

        // Stream rows
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, object?>(columnCount);
            for (int i = 0; i < columnCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                values[columnNames[i]] = value;
            }
            yield return new ResultRow(values);
        }
    }

    public async Task<QueryResult> ExecutePageAsync(
        string sql,
        QueryOptions options,
        int? offset = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new ArgumentException("SQL query cannot be empty.", nameof(sql));
        }

        var dbStopwatch = Stopwatch.StartNew();
        var fetchStopwatch = new Stopwatch();

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(cancellationToken);

        // Add pagination if offset is specified
        var paginatedSql = sql;
        if (offset.HasValue)
        {
            paginatedSql = $"{sql} LIMIT {options.PageSize} OFFSET {offset.Value}";
        }
        else if (!sql.TrimEnd().EndsWith(";", StringComparison.OrdinalIgnoreCase))
        {
            // Add limit for first page if not already present
            paginatedSql = $"{sql} LIMIT {options.PageSize}";
        }

        using var command = connection.CreateCommand();
        command.CommandText = paginatedSql;

        using var reader = await command.ExecuteReaderAsync(cancellationToken);

        dbStopwatch.Stop();
        fetchStopwatch.Start();

        // Get column information
        var columns = new List<ColumnInfo>();
        for (int i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            var type = reader.GetFieldType(i);
            columns.Add(new ColumnInfo(name, type?.Name ?? "unknown"));
        }

        // Read rows
        var rows = new List<ResultRow>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var values = new Dictionary<string, object?>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                values[reader.GetName(i)] = value;
            }
            rows.Add(new ResultRow(values));
        }

        fetchStopwatch.Stop();

        // Get total count - skip if query already has LIMIT/OFFSET
        var totalCount = sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase) 
            ? rows.Count 
            : await GetTotalCountAsync(sql, connection, cancellationToken);

        var timings = new QueryTimings(
            DbExecutionTime: dbStopwatch.Elapsed,
            FetchTime: fetchStopwatch.Elapsed,
            UiBindTime: null);

        return new QueryResult(columns, rows, timings, totalCount);
    }

    private static async Task<int> GetTotalCountAsync(string sql, DecentDBConnection connection, CancellationToken ct)
    {
        // DecentDB doesn't support subqueries in COUNT, so we return -1 to indicate unknown
        // In a production implementation, you might parse the SQL to extract the FROM clause
        // or use a different approach (e.g., separate count query with same WHERE clause)
        return -1;
    }
}
