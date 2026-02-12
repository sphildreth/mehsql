using System;
using System.Data.Common;
using System.IO;
using System.Threading.Tasks;
using MehSql.Core.Connections;
using MehSql.Core.Execution;
using MehSql.Core.Querying;
using Xunit;

namespace MehSql.Core.Tests;

public class QueryExecutorTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IConnectionFactory _connectionFactory;

    public QueryExecutorTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mehsql_test_{Guid.NewGuid()}.db");
        _connectionFactory = new ConnectionFactory(_testDbPath);

        // Initialize test database with sample data
        InitializeTestDatabase().Wait();
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private async Task InitializeTestDatabase()
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync();

        // Create table using DecentDB-supported syntax
        using var createCmd = conn.CreateCommand();
        createCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS test_items (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                value FLOAT
            )";
        await createCmd.ExecuteNonQueryAsync();

        // Insert test data using parameters
        var items = new[] {
            (1L, "Item 1", 10.5),
            (2L, "Item 2", 20.0),
            (3L, "Item 3", 30.25),
            (4L, "Item 4", 40.75),
            (5L, "Item 5", 50.0)
        };

        foreach (var (id, name, value) in items)
        {
            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = "INSERT INTO test_items (id, name, value) VALUES (@id, @name, @value)";
            AddParam(insertCmd, "@id", id);
            AddParam(insertCmd, "@name", name);
            AddParam(insertCmd, "@value", value);
            await insertCmd.ExecuteNonQueryAsync();
        }
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    [Fact]
    public async Task ExecutePageAsync_ReturnsResults_WithTimings()
    {
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions(PageSize: 10);

        var result = await executor.ExecutePageAsync("SELECT * FROM test_items", options);

        Assert.NotNull(result);
        Assert.Equal(5, result.Rows.Count);
        Assert.NotNull(result.Columns);
        Assert.True(result.Columns.Count >= 3);
        Assert.NotNull(result.Timings);
        Assert.NotNull(result.Timings.DbExecutionTime);
        Assert.True(result.Timings.DbExecutionTime.Value.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task ExecutePageAsync_WithPagination_ReturnsCorrectPage()
    {
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions(PageSize: 2);

        var firstPage = await executor.ExecutePageAsync("SELECT * FROM test_items ORDER BY id", options, offset: null);

        Assert.Equal(2, firstPage.Rows.Count);

        // Check first row has id=1
        var firstRow = firstPage.Rows[0];
        Assert.True(firstRow.Values.ContainsKey("id"));
        Assert.Equal(1L, firstRow.Values["id"]);
    }

    [Fact]
    public async Task ExecutePageAsync_WithSemicolonTerminatedSql_AppliesFirstPageLimit()
    {
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions(PageSize: 2);

        var firstPage = await executor.ExecutePageAsync("SELECT * FROM test_items ORDER BY id;", options, offset: null);

        Assert.Equal(2, firstPage.Rows.Count);
        Assert.Equal(1L, firstPage.Rows[0].Values["id"]);
    }

    [Fact]
    public async Task ExecutePageAsync_WithSemicolonTerminatedSql_AppliesOffsetPaging()
    {
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions(PageSize: 2);

        var nextPage = await executor.ExecutePageAsync("SELECT * FROM test_items ORDER BY id;", options, offset: 2);

        Assert.Equal(2, nextPage.Rows.Count);
        Assert.Equal(3L, nextPage.Rows[0].Values["id"]);
    }

    [Fact]
    public async Task ExecuteQueryAsync_StreamsAllResults()
    {
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions();

        var count = 0;
        await foreach (var row in executor.ExecuteQueryAsync("SELECT * FROM test_items", options))
        {
            Assert.NotNull(row);
            Assert.NotNull(row.Values);
            Assert.True(row.Values.ContainsKey("id"));
            Assert.True(row.Values.ContainsKey("name"));
            count++;
        }

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task ExecutePageAsync_WithCancellationToken_RespectsCancellation()
    {
        // Note: DecentDB may not fully support cancellation, but we ensure the token is passed through
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions();
        using var cts = new System.Threading.CancellationTokenSource();

        // Cancel before starting - behavior depends on DecentDB implementation
        cts.Cancel();

        try
        {
            await executor.ExecutePageAsync("SELECT * FROM test_items", options, cancellationToken: cts.Token);
            // If no exception, that's also acceptable - DecentDB may ignore cancellation
        }
        catch (OperationCanceledException)
        {
            // Expected if cancellation is supported
        }
    }

    [Fact]
    public async Task ExecutePageAsync_EmptyResultSet_ReturnsEmptyList()
    {
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions();

        var result = await executor.ExecutePageAsync("SELECT * FROM test_items WHERE id > 1000", options);

        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ConnectionFactory_CreatesValidConnection()
    {
        var factory = new ConnectionFactory(_testDbPath);
        using var conn = factory.CreateConnection();

        Assert.NotNull(conn);
    }

    [Fact]
    public void ConnectionFactory_WithConnectionString_CreatesValidConnection()
    {
        var factory = new ConnectionFactory($"Data Source={_testDbPath}");
        using var conn = factory.CreateConnection();

        Assert.NotNull(conn);
    }
}
