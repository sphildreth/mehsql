using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using MehSql.Core.Connections;
using MehSql.Core.Schema;
using Xunit;

namespace MehSql.Core.Tests;

/// <summary>
/// Tests for SchemaService.
/// Note: These tests verify the stub implementation behavior.
/// Full introspection tests require DecentDB to expose catalog tables.
/// </summary>
public class SchemaServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IConnectionFactory _connectionFactory;

    public SchemaServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mehsql_schema_test_{Guid.NewGuid()}.db");
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
    public async Task GetTablesAsync_ReturnsEmptyList_StubImplementation()
    {
        var service = new SchemaService(_connectionFactory);
        var tables = await service.GetTablesAsync();

        // Stub implementation returns empty list
        Assert.Empty(tables);
    }

    [Fact]
    public async Task GetViewsAsync_ReturnsEmptyList_StubImplementation()
    {
        var service = new SchemaService(_connectionFactory);
        var views = await service.GetViewsAsync();

        // Stub implementation returns empty list
        Assert.Empty(views);
    }

    [Fact]
    public async Task GetTableColumnsAsync_ReturnsEmptyList_StubImplementation()
    {
        var service = new SchemaService(_connectionFactory);
        var columns = await service.GetTableColumnsAsync("public", "users");

        // Stub implementation returns empty list
        Assert.Empty(columns);
    }

    [Fact]
    public async Task GetTableIndexesAsync_ReturnsEmptyList_StubImplementation()
    {
        var service = new SchemaService(_connectionFactory);
        var indexes = await service.GetTableIndexesAsync("public", "posts");

        // Stub implementation returns empty list
        Assert.Empty(indexes);
    }

    [Fact]
    public async Task GetSchemaAsync_ReturnsEmptySchema_StubImplementation()
    {
        var service = new SchemaService(_connectionFactory);
        var schema = await service.GetSchemaAsync();

        // Stub implementation returns empty schema
        Assert.NotNull(schema);
        Assert.Empty(schema.Tables);
        Assert.Empty(schema.Views);
    }

    [Fact]
    public async Task GetTableForeignKeysAsync_ReturnsEmptyList_WhenCatalogTableUnavailable()
    {
        var service = new SchemaService(_connectionFactory);
        var foreignKeys = await service.GetTableForeignKeysAsync("main", "users");

        Assert.Empty(foreignKeys);
    }

    [Fact]
    public async Task GetTableTriggersAsync_ReturnsEmptyList_WhenCatalogTableUnavailable()
    {
        var service = new SchemaService(_connectionFactory);
        var triggers = await service.GetTableTriggersAsync("main", "users");

        Assert.Empty(triggers);
    }

    [Fact]
    public async Task GetViewTriggersAsync_ReturnsEmptyList_WhenCatalogTableUnavailable()
    {
        var service = new SchemaService(_connectionFactory);
        var triggers = await service.GetViewTriggersAsync("main", "users_view");

        Assert.Empty(triggers);
    }

    [Fact]
    public void SchemaNode_CreatesCorrectly()
    {
        var table = new TableNode("public", "users");
        Assert.Equal("users", table.Name);
        Assert.Equal("public", table.Schema);
        Assert.Equal("Table", table.NodeType);
    }

    [Fact]
    public void ColumnNode_CreatesCorrectly()
    {
        var col = new ColumnNode("id", "INTEGER", false, null, true);
        Assert.Equal("id", col.Name);
        Assert.Equal("INTEGER", col.DataType);
        Assert.False(col.IsNullable);
        Assert.True(col.IsPrimaryKey);
        Assert.Equal("id : INTEGER", col.DisplayText);
    }

    [Fact]
    public void IndexNode_CreatesCorrectly()
    {
        var idx = new IndexNode("idx_users", true, new List<string> { "id", "name" });
        Assert.Equal("idx_users", idx.Name);
        Assert.True(idx.IsUnique);
        Assert.Equal(2, idx.Columns.Count);
    }
}
