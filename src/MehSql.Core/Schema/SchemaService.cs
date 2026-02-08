using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MehSql.Core.Connections;

namespace MehSql.Core.Schema;

/// <summary>
/// Service for introspecting database schema (tables, views, columns, indexes).
/// </summary>
public interface ISchemaService
{
    /// <summary>
    /// Gets all schema information for the connected database.
    /// </summary>
    Task<SchemaRootNode> GetSchemaAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all tables in the database.
    /// </summary>
    Task<IReadOnlyList<TableNode>> GetTablesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets all views in the database.
    /// </summary>
    Task<IReadOnlyList<ViewNode>> GetViewsAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets columns for a specific table.
    /// </summary>
    Task<IReadOnlyList<ColumnNode>> GetTableColumnsAsync(string schema, string tableName, CancellationToken ct = default);

    /// <summary>
    /// Gets indexes for a specific table.
    /// </summary>
    Task<IReadOnlyList<IndexNode>> GetTableIndexesAsync(string schema, string tableName, CancellationToken ct = default);
}

/// <summary>
/// Stub implementation for Phase 4.
/// Note: Full introspection requires DecentDB to expose catalog tables.
/// Currently returns empty lists - UI structure is in place for when backend supports it.
/// </summary>
public sealed class SchemaService : ISchemaService
{
    private readonly IConnectionFactory _connectionFactory;

    public SchemaService(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public Task<SchemaRootNode> GetSchemaAsync(CancellationToken ct = default)
    {
        // Return empty schema - DecentDB doesn't expose catalog tables yet
        var root = new SchemaRootNode("main");
        return Task.FromResult(root);
    }

    public Task<IReadOnlyList<TableNode>> GetTablesAsync(CancellationToken ct = default)
    {
        // DecentDB doesn't expose pg_catalog or information_schema tables
        // This would require DecentDB to implement catalog introspection
        return Task.FromResult<IReadOnlyList<TableNode>>(new List<TableNode>());
    }

    public Task<IReadOnlyList<ViewNode>> GetViewsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<ViewNode>>(new List<ViewNode>());
    }

    public Task<IReadOnlyList<ColumnNode>> GetTableColumnsAsync(string schema, string tableName, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<ColumnNode>>(new List<ColumnNode>());
    }

    public Task<IReadOnlyList<IndexNode>> GetTableIndexesAsync(string schema, string tableName, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<IndexNode>>(new List<IndexNode>());
    }
}
