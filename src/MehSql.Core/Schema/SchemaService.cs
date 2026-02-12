using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MehSql.Core.Connections;
using DecentDB.AdoNet;
using Serilog;

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

    /// <summary>
    /// Gets foreign keys for a specific table.
    /// </summary>
    Task<IReadOnlyList<ForeignKeyNode>> GetTableForeignKeysAsync(string schema, string tableName, CancellationToken ct = default);

    /// <summary>
    /// Gets triggers attached to a specific table.
    /// </summary>
    Task<IReadOnlyList<TriggerNode>> GetTableTriggersAsync(string schema, string tableName, CancellationToken ct = default);

    /// <summary>
    /// Gets triggers attached to a specific view.
    /// </summary>
    Task<IReadOnlyList<TriggerNode>> GetViewTriggersAsync(string schema, string viewName, CancellationToken ct = default);
}

/// <summary>
/// Implementation of ISchemaService that retrieves schema information from DecentDB.
/// Uses GetSchema() method to introspect the database structure.
/// </summary>
public sealed class SchemaService : ISchemaService
{
    private readonly IConnectionFactory _connectionFactory;
    private HashSet<string>? _catalogTableNames;

    public SchemaService(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        Log.Logger.Information("SchemaService initialized with connection factory");
    }

    public async Task<SchemaRootNode> GetSchemaAsync(CancellationToken ct = default)
    {
        Log.Logger.Debug("GetSchemaAsync called with cancellation token");
        var root = new SchemaRootNode("main");
        
        // Get all tables
        var tables = await GetTablesAsync(ct);
        Log.Logger.Debug("Retrieved {TableCount} tables", tables.Count);
        root.Tables.AddRange(tables);
        
        // Get all views
        var views = await GetViewsAsync(ct);
        Log.Logger.Debug("Retrieved {ViewCount} views", views.Count);
        root.Views.AddRange(views);
        
        Log.Logger.Information("Schema retrieved with {TableCount} tables and {ViewCount} views", 
            root.Tables.Count, root.Views.Count);
        return root;
    }

    public async Task<IReadOnlyList<TableNode>> GetTablesAsync(CancellationToken ct = default)
    {
        Log.Logger.Debug("GetTablesAsync called with cancellation token");
        var tables = new List<TableNode>();
        
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        Log.Logger.Debug("Connection opened for retrieving tables");

        // Use DecentDB's GetSchema to get table information
        // Need to cast to DecentDBConnection to access GetSchema method
        var decentDbConnection = connection as DecentDBConnection;
        if (decentDbConnection == null)
        {
            Log.Logger.Error("Connection is not a DecentDBConnection, cannot access schema information");
            throw new InvalidOperationException("Connection must be a DecentDBConnection to access schema information.");
        }
        
        Log.Logger.Debug("Getting 'Tables' schema information");
        var dataTable = decentDbConnection.GetSchema("Tables");
        CacheCatalogTableNames(dataTable);
        Log.Logger.Debug("Retrieved schema data table with {RowCount} rows", dataTable.Rows.Count);
        
        foreach (DataRow row in dataTable.Rows)
        {
            var tableName = (string)row["TABLE_NAME"];
            Log.Logger.Debug("Processing table: {TableName}", tableName);
            
            var table = new TableNode("main", tableName);
            
            // Get columns for this table
            var columns = await GetTableColumnsAsync("main", tableName, ct);
            Log.Logger.Debug("Retrieved {ColumnCount} columns for table {TableName}", columns.Count, tableName);
            table.Columns.AddRange(columns);
            
            // Get indexes for this table
            var indexes = await GetTableIndexesAsync("main", tableName, ct);
            Log.Logger.Debug("Retrieved {IndexCount} indexes for table {TableName}", indexes.Count, tableName);
            table.Indexes.AddRange(indexes);

            var foreignKeys = await GetTableForeignKeysAsync("main", tableName, ct);
            table.ForeignKeys.AddRange(foreignKeys);

            var triggers = await GetTableTriggersAsync("main", tableName, ct);
            table.Triggers.AddRange(triggers);
            
            tables.Add(table);
        }

        Log.Logger.Information("Successfully retrieved {TableCount} tables", tables.Count);
        return tables;
    }

    public async Task<IReadOnlyList<ViewNode>> GetViewsAsync(CancellationToken ct = default)
    {
        Log.Logger.Debug("GetViewsAsync called");
        var views = new List<ViewNode>();

        if (!await CatalogTableExistsAsync("views", ct))
        {
            Log.Logger.Debug("Skipping view introspection because catalog table 'views' is unavailable");
            Log.Logger.Information("Successfully retrieved {ViewCount} views", views.Count);
            return views;
        }

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(ct);

            // DecentDB supports views but GetSchema("Views") isn't implemented.
            // Query the internal views catalog via SQL instead.
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT name FROM views ORDER BY name";
            using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var viewName = reader.GetString(0);
                Log.Logger.Debug("Found view: {ViewName}", viewName);
                var view = new ViewNode("main", viewName);
                try
                {
                    var viewColumns = await GetTableColumnsAsync("main", viewName, ct);
                    view.Columns.AddRange(viewColumns);
                }
                catch (Exception ex)
                {
                    Log.Logger.Debug(ex, "Could not load columns for view {ViewName}", viewName);
                }

                var viewTriggers = await GetViewTriggersAsync("main", viewName, ct);
                view.Triggers.AddRange(viewTriggers);
                views.Add(view);
            }
        }
        catch (Exception ex)
        {
            // DecentDB may not support the views catalog query yet — fail gracefully
            Log.Logger.Debug(ex, "Could not query views catalog: {Message}", ex.Message);
        }

        Log.Logger.Information("Successfully retrieved {ViewCount} views", views.Count);
        return views;
    }

    public async Task<IReadOnlyList<ColumnNode>> GetTableColumnsAsync(string schema, string tableName, CancellationToken ct = default)
    {
        Log.Logger.Debug("GetTableColumnsAsync called for table: {TableName}, schema: {Schema}", tableName, schema);
        var columns = new List<ColumnNode>();
        
        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);
        Log.Logger.Debug("Connection opened for retrieving columns for table: {TableName}", tableName);

        // Use DecentDB's GetSchema to get column information for the specific table
        // Need to cast to DecentDBConnection to access GetSchema method
        var decentDbConnection = connection as DecentDBConnection;
        if (decentDbConnection == null)
        {
            Log.Logger.Error("Connection is not a DecentDBConnection, cannot access schema information for table: {TableName}", tableName);
            throw new InvalidOperationException("Connection must be a DecentDBConnection to access schema information.");
        }
        
        Log.Logger.Debug("Getting 'Columns' schema information for table: {TableName}", tableName);
        var dataTable = decentDbConnection.GetSchema("Columns", new[] { tableName });
        Log.Logger.Debug("Retrieved column schema data table with {RowCount} rows for table: {TableName}", dataTable.Rows.Count, tableName);
        
        foreach (DataRow row in dataTable.Rows)
        {
            var columnName = (string)row["COLUMN_NAME"];
            var dataType = (string)row["DATA_TYPE"];
            var isNullable = (bool)row["IS_NULLABLE"];
            var isPrimaryKey = row.Table.Columns.Contains("IS_PRIMARY_KEY") ? (bool)row["IS_PRIMARY_KEY"] : false;
            var defaultValue = row.Table.Columns.Contains("COLUMN_DEFAULT") && !row.IsNull("COLUMN_DEFAULT") 
                ? row["COLUMN_DEFAULT"].ToString() 
                : null;

            Log.Logger.Debug("Processing column: {ColumnName}, Type: {DataType}, Nullable: {IsNullable}, PK: {IsPrimaryKey}", 
                columnName, dataType, isNullable, isPrimaryKey);
                
            var column = new ColumnNode(columnName, dataType, isNullable, defaultValue, isPrimaryKey);
            columns.Add(column);
        }

        Log.Logger.Information("Successfully retrieved {ColumnCount} columns for table: {TableName}", columns.Count, tableName);
        return columns;
    }

    public async Task<IReadOnlyList<IndexNode>> GetTableIndexesAsync(string schema, string tableName, CancellationToken ct = default)
    {
        Log.Logger.Debug("GetTableIndexesAsync called for table: {TableName}, schema: {Schema}", tableName, schema);
        var indexes = new List<IndexNode>();

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(ct);

            var decentDbConnection = connection as DecentDBConnection;
            if (decentDbConnection is null)
            {
                Log.Logger.Error("Connection is not a DecentDBConnection, cannot access index information");
                return indexes;
            }

            var dataTable = decentDbConnection.GetSchema("Indexes", new[] { tableName });
            foreach (DataRow row in dataTable.Rows)
            {
                var indexName = (string)row["INDEX_NAME"];
                var isUnique = (bool)row["IS_UNIQUE"];
                var columnsStr = (string)row["COLUMNS"];
                var columnNames = columnsStr.Split(',', StringSplitOptions.TrimEntries).ToList();

                indexes.Add(new IndexNode(indexName, isUnique, columnNames));
                Log.Logger.Debug("Found index: {IndexName}, Unique: {IsUnique}, Columns: {Columns}",
                    indexName, isUnique, columnsStr);
            }

            Log.Logger.Information("Successfully retrieved {IndexCount} indexes for table: {TableName}", indexes.Count, tableName);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to retrieve indexes for table {TableName}", tableName);
        }

        return indexes;
    }

    public async Task<IReadOnlyList<ForeignKeyNode>> GetTableForeignKeysAsync(string schema, string tableName, CancellationToken ct = default)
    {
        var foreignKeys = new List<ForeignKeyNode>();

        if (!await CatalogTableExistsAsync("foreign_keys", ct))
        {
            return foreignKeys;
        }

        var escapedTable = EscapeSqlLiteral(tableName);
        var sql = $@"
SELECT
    constraint_name,
    column_name,
    foreign_table_name,
    foreign_column_name
FROM foreign_keys
WHERE table_name = '{escapedTable}'
ORDER BY constraint_name, column_name";

        await TryReadNodesAsync(
            sql,
            reader =>
            {
                var name = ReadString(reader, 0, fallback: $"fk_{tableName}");
                var column = ReadString(reader, 1, fallback: "unknown_column");
                var refTable = ReadString(reader, 2, fallback: "unknown_table");
                var refColumn = ReadString(reader, 3, fallback: "unknown_column");
                foreignKeys.Add(new ForeignKeyNode(name, column, refTable, refColumn));
            },
            ct);

        return foreignKeys;
    }

    public async Task<IReadOnlyList<TriggerNode>> GetTableTriggersAsync(string schema, string tableName, CancellationToken ct = default)
    {
        return await GetTriggersByParentAsync(tableName, ct);
    }

    public async Task<IReadOnlyList<TriggerNode>> GetViewTriggersAsync(string schema, string viewName, CancellationToken ct = default)
    {
        return await GetTriggersByParentAsync(viewName, ct);
    }

    private async Task<IReadOnlyList<TriggerNode>> GetTriggersByParentAsync(string parentName, CancellationToken ct)
    {
        var triggers = new List<TriggerNode>();

        if (!await CatalogTableExistsAsync("triggers", ct))
        {
            return triggers;
        }

        var escapedName = EscapeSqlLiteral(parentName);

        var possibleQueries = new[]
        {
            $@"
SELECT
    name,
    timing,
    event,
    table_name,
    sql
FROM triggers
WHERE table_name = '{escapedName}'
ORDER BY name",
            $@"
SELECT
    name,
    timing,
    event,
    view_name,
    sql
FROM triggers
WHERE view_name = '{escapedName}'
ORDER BY name"
        };

        foreach (var sql in possibleQueries)
        {
            var readAny = await TryReadNodesAsync(
                sql,
                reader =>
                {
                    var name = ReadString(reader, 0, fallback: "trigger");
                    var timing = ReadString(reader, 1, fallback: "AFTER");
                    var triggerEvent = ReadString(reader, 2, fallback: "UNKNOWN");
                    var parent = ReadString(reader, 3, fallback: parentName);
                    var sourceSql = reader.FieldCount > 4 && !reader.IsDBNull(4) ? reader.GetValue(4)?.ToString() : null;

                    triggers.Add(new TriggerNode(name, timing, triggerEvent, parent, sourceSql));
                },
                ct);

            if (readAny)
            {
                break;
            }
        }

        return triggers;
    }

    private async Task<bool> TryReadNodesAsync(
        string sql,
        Action<IDataRecord> onRow,
        CancellationToken ct)
    {
        try
        {
            using var connection = _connectionFactory.CreateConnection();
            await connection.OpenAsync(ct);

            using var cmd = connection.CreateCommand();
            cmd.CommandText = sql;
            using var reader = await cmd.ExecuteReaderAsync(ct);

            var readAny = false;
            while (await reader.ReadAsync(ct))
            {
                readAny = true;
                onRow(reader);
            }

            return readAny;
        }
        catch (Exception ex)
        {
            Log.Logger.Debug(ex, "Catalog query failed: {Sql}", sql);
            return false;
        }
    }

    private static string ReadString(IDataRecord reader, int ordinal, string fallback)
    {
        if (ordinal >= reader.FieldCount || reader.IsDBNull(ordinal))
        {
            return fallback;
        }

        return reader.GetValue(ordinal)?.ToString() ?? fallback;
    }

    private async Task<bool> CatalogTableExistsAsync(string tableName, CancellationToken ct)
    {
        var tableNames = await GetCatalogTableNamesAsync(ct);
        return tableNames.Contains(tableName);
    }

    private async Task<HashSet<string>> GetCatalogTableNamesAsync(CancellationToken ct)
    {
        if (_catalogTableNames is not null)
        {
            return _catalogTableNames;
        }

        using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync(ct);

        if (connection is not DecentDBConnection decentDbConnection)
        {
            Log.Logger.Error("Connection is not a DecentDBConnection, cannot access schema information");
            throw new InvalidOperationException("Connection must be a DecentDBConnection to access schema information.");
        }

        var dataTable = decentDbConnection.GetSchema("Tables");
        CacheCatalogTableNames(dataTable);
        return _catalogTableNames ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private void CacheCatalogTableNames(DataTable dataTable)
    {
        if (_catalogTableNames is not null)
        {
            return;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!dataTable.Columns.Contains("TABLE_NAME"))
        {
            _catalogTableNames = names;
            return;
        }

        foreach (DataRow row in dataTable.Rows)
        {
            var name = row["TABLE_NAME"]?.ToString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        _catalogTableNames = names;
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''", StringComparison.Ordinal);
}
