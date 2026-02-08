using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MehSql.Core.Export;
using MehSql.Core.Querying;
using Xunit;

namespace MehSql.Core.Tests;

public class ExportServiceTests
{
    private readonly ExportService _exportService = new();

    [Fact]
    public async Task ExportToCsvAsync_WithSimpleData_WritesCorrectCsv()
    {
        var pages = GetTestPagesAsync();
        using var stream = new MemoryStream();
        var options = new ExportOptions { IncludeHeaders = true };

        await _exportService.ExportToCsvAsync(pages, stream, options);

        stream.Position = 0;
        var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("id,name", content);
        Assert.Contains("1,Alice", content);
        Assert.Contains("2,Bob", content);
    }

    [Fact]
    public async Task ExportToCsvAsync_WithoutHeaders_WritesDataOnly()
    {
        var pages = GetTestPagesAsync();
        using var stream = new MemoryStream();
        var options = new ExportOptions { IncludeHeaders = false };

        await _exportService.ExportToCsvAsync(pages, stream, options);

        stream.Position = 0;
        var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        Assert.DoesNotContain("id,name", content);
        Assert.Contains("1,Alice", content);
        Assert.Contains("2,Bob", content);
    }

    [Fact]
    public async Task ExportToCsvAsync_EscapesSpecialCharacters()
    {
        var pages = GetPagesWithSpecialCharactersAsync();
        using var stream = new MemoryStream();
        var options = new ExportOptions { IncludeHeaders = true };

        await _exportService.ExportToCsvAsync(pages, stream, options);

        stream.Position = 0;
        var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        // Should escape commas and quotes
        Assert.Contains("\"Hello, World\"", content);
        Assert.Contains("\"Say \"\"hello\"\"\"", content);
    }

    [Fact]
    public async Task ExportToJsonAsync_WritesValidJson()
    {
        var pages = GetTestPagesAsync();
        using var stream = new MemoryStream();
        var options = new ExportOptions { IncludeHeaders = false };

        await _exportService.ExportToJsonAsync(pages, stream, options);

        stream.Position = 0;
        var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        Assert.StartsWith("[", content);
        Assert.EndsWith("]", content);
        Assert.Contains("\"id\":1", content);
        Assert.Contains("\"name\":\"Alice\"", content);
    }

    [Fact]
    public async Task ExportToJsonAsync_FormatsDatesCorrectly()
    {
        var pages = GetPagesWithDatesAsync();
        using var stream = new MemoryStream();
        var options = new ExportOptions { FormatDatesAsIso = true, DateTimeFormat = "yyyy-MM-dd" };

        await _exportService.ExportToJsonAsync(pages, stream, options);

        stream.Position = 0;
        var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("2024-01-15", content);
    }

    [Fact]
    public async Task ExportToJsonAsync_HandlesNullValues()
    {
        var pages = GetPagesWithNullsAsync();
        using var stream = new MemoryStream();
        var options = new ExportOptions();

        await _exportService.ExportToJsonAsync(pages, stream, options);

        stream.Position = 0;
        var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync();

        Assert.Contains("null", content);
    }

    private static async IAsyncEnumerable<QueryPage> GetTestPagesAsync()
    {
        var columns = new[] { new ColumnInfo("id", "bigint"), new ColumnInfo("name", "text") };
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["id"] = 1L, ["name"] = "Alice" },
            new Dictionary<string, object?> { ["id"] = 2L, ["name"] = "Bob" }
        };

        yield return new QueryPage(columns, rows, null, new QueryTimings(null, TimeSpan.Zero, null));
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<QueryPage> GetPagesWithSpecialCharactersAsync()
    {
        var columns = new[] { new ColumnInfo("text", "text") };
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["text"] = "Hello, World" },
            new Dictionary<string, object?> { ["text"] = "Say \"hello\"" }
        };

        yield return new QueryPage(columns, rows, null, new QueryTimings(null, TimeSpan.Zero, null));
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<QueryPage> GetPagesWithDatesAsync()
    {
        var columns = new[] { new ColumnInfo("date", "timestamp") };
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["date"] = new DateTime(2024, 1, 15) }
        };

        yield return new QueryPage(columns, rows, null, new QueryTimings(null, TimeSpan.Zero, null));
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<QueryPage> GetPagesWithNullsAsync()
    {
        var columns = new[] { new ColumnInfo("value", "text") };
        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["value"] = null },
            new Dictionary<string, object?> { ["value"] = "not null" }
        };

        yield return new QueryPage(columns, rows, null, new QueryTimings(null, TimeSpan.Zero, null));
        await Task.CompletedTask;
    }
}
