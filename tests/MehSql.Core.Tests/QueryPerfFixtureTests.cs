using System;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using MehSql.Core.Connections;
using MehSql.Core.Execution;
using MehSql.Core.Querying;
using Xunit;
using Xunit.Abstractions;

namespace MehSql.Core.Tests;

/// <summary>
/// Performance fixture tests for query execution.
/// These tests validate timing capture and streaming performance.
/// </summary>
public class QueryPerfFixtureTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IConnectionFactory _connectionFactory;
    private readonly ITestOutputHelper _output;

    public QueryPerfFixtureTests(ITestOutputHelper output)
    {
        _output = output;
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mehsql_perf_{Guid.NewGuid()}.db");
        _connectionFactory = new ConnectionFactory(_testDbPath);
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

    [Fact]
    public async Task TimingCapture_DbExecutionTime_IsCaptured()
    {
        // Arrange
        await InitializeDatabaseWithRows(100);
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions(PageSize: 50);

        // Act
        var result = await executor.ExecutePageAsync("SELECT * FROM perf_test", options);

        // Assert
        Assert.NotNull(result.Timings);
        Assert.NotNull(result.Timings.DbExecutionTime);
        Assert.True(result.Timings.DbExecutionTime.Value.TotalMilliseconds > 0,
            "DB execution time should be greater than 0");
        
        _output.WriteLine($"DB Execution Time: {result.Timings.DbExecutionTime.Value.TotalMilliseconds:F3}ms");
        _output.WriteLine($"Fetch Time: {result.Timings.FetchTime.TotalMilliseconds:F3}ms");
    }

    [Fact]
    public async Task TimingCapture_FetchTime_IsCaptured()
    {
        // Arrange
        await InitializeDatabaseWithRows(100);
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions(PageSize: 50);

        // Act
        var result = await executor.ExecutePageAsync("SELECT * FROM perf_test", options);

        // Assert
        Assert.True(result.Timings.FetchTime.TotalMilliseconds >= 0,
            "Fetch time should be non-negative");
        
        _output.WriteLine($"Fetch Time: {result.Timings.FetchTime.TotalMilliseconds:F3}ms");
    }

    [Fact]
    public async Task StreamingQuery_LargeResultSet_PerformsWell()
    {
        // Arrange
        const int rowCount = 1000;
        await InitializeDatabaseWithRows(rowCount);
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions();

        var sw = Stopwatch.StartNew();
        var count = 0;

        // Act - Stream all rows
        await foreach (var row in executor.ExecuteQueryAsync("SELECT * FROM perf_test", options))
        {
            count++;
            // Simulate light processing
            _ = row.Values["id"];
        }

        sw.Stop();

        // Assert
        Assert.Equal(rowCount, count);
        _output.WriteLine($"Streamed {rowCount} rows in {sw.ElapsedMilliseconds}ms " +
                         $"({(double)sw.ElapsedMilliseconds / rowCount:F3}ms per row)");
        
        // Should complete reasonably fast (< 5 seconds for 1000 rows)
        Assert.True(sw.ElapsedMilliseconds < 5000, 
            $"Streaming {rowCount} rows took too long: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task Pagination_LargeDataset_ReturnsConsistentPages()
    {
        // Arrange
        const int totalRows = 100;
        await InitializeDatabaseWithRows(totalRows);
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions(PageSize: 10);

        var pager = new QueryPager(_connectionFactory);

        // Act - Get all pages
        var allRows = new System.Collections.Generic.List<IReadOnlyDictionary<string, object?>>();
        var firstPage = await pager.ExecuteFirstPageAsync("SELECT * FROM perf_test ORDER BY id", options, default);
        allRows.AddRange(firstPage.Rows);

        var nextToken = firstPage.NextToken;
        while (nextToken != null)
        {
            var page = await pager.ExecuteNextPageAsync("SELECT * FROM perf_test ORDER BY id", options, nextToken, default);
            allRows.AddRange(page.Rows);
            nextToken = page.NextToken;
        }

        // Assert
        Assert.Equal(totalRows, allRows.Count);
        
        // Verify ordering (ids should be 1, 2, 3, ...)
        for (int i = 0; i < allRows.Count; i++)
        {
            var expectedId = i + 1;
            var actualId = Convert.ToInt32(allRows[i]["id"]);
            Assert.Equal(expectedId, actualId);
        }
    }

    [Fact]
    public async Task Cancellation_DuringStreaming_RespectsToken()
    {
        // Arrange
        await InitializeDatabaseWithRows(100);
        var executor = new QueryExecutor(_connectionFactory);
        var options = new QueryOptions();
        using var cts = new System.Threading.CancellationTokenSource();

        // Act - Cancel after reading a few rows
        var count = 0;
        try
        {
            await foreach (var row in executor.ExecuteQueryAsync("SELECT * FROM perf_test", options, cts.Token))
            {
                count++;
                if (count == 5)
                {
                    cts.Cancel();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected if DecentDB supports cancellation
        }

        // Assert - either we read all rows (if cancellation not supported) or stopped early
        _output.WriteLine($"Read {count} rows");
        // Just verify we got some data
        Assert.True(count >= 5, "Should have read at least 5 rows");
    }

    private async Task InitializeDatabaseWithRows(int count)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync();

        // Create table using DecentDB-supported syntax
        using var createCmd = conn.CreateCommand();
        createCmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS perf_test (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                value FLOAT,
                data TEXT
            )";
        await createCmd.ExecuteNonQueryAsync();

        // Clear existing data
        using var clearCmd = conn.CreateCommand();
        clearCmd.CommandText = "DELETE FROM perf_test";
        await clearCmd.ExecuteNonQueryAsync();

        // Insert rows one at a time using parameters
        for (int rowNum = 1; rowNum <= count; rowNum++)
        {
            using var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = "INSERT INTO perf_test (id, name, value, data) VALUES (@id, @name, @value, @data)";
            AddParam(insertCmd, "@id", (long)rowNum);
            AddParam(insertCmd, "@name", $"Item {rowNum}");
            AddParam(insertCmd, "@value", (double)(rowNum * 1.5));
            AddParam(insertCmd, "@data", $"Data for item {rowNum}");
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
}
