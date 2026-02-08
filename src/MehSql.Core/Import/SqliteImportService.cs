using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using Serilog;

namespace MehSql.Core.Import;

/// <summary>
/// Imports a SQLite database into a DecentDB database file.
/// Faithfully ports the logic from the Python sqlite_import.py tool.
/// </summary>
public sealed class SqliteImportService
{
    private const int FetchBatchSize = 1_000;

    /// <summary>
    /// Analyze a SQLite database and return schema + row counts without modifying anything.
    /// </summary>
    public async Task<AnalysisResult> AnalyzeAsync(string sqlitePath, CancellationToken ct = default)
    {
        if (!File.Exists(sqlitePath))
            throw new FileNotFoundException("SQLite file not found.", sqlitePath);

        await using var conn = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly");
        await conn.OpenAsync(ct);
        conn.CreateCommand().Apply(c => { c.CommandText = "PRAGMA foreign_keys=ON"; c.ExecuteNonQuery(); });

        var tables = LoadAllTableSchemas(conn);
        foreach (var t in tables)
            ValidateSupported(t);

        var rowCounts = new Dictionary<string, long>();
        foreach (var t in tables)
        {
            rowCounts[t.Name] = GetTableRowCount(conn, t.Name);
        }

        var skipped = tables.SelectMany(t => t.SkippedIndexes).ToList();
        var warnings = new List<string>();

        return new AnalysisResult
        {
            SqlitePath = sqlitePath,
            Tables = tables,
            RowCounts = rowCounts,
            SkippedIndexes = skipped,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Run the full import pipeline: create schema, copy data, create indexes.
    /// </summary>
    public async Task<ImportReport> ImportAsync(
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(options.SqlitePath))
            throw new FileNotFoundException("SQLite file not found.", options.SqlitePath);

        if (File.Exists(options.DecentDbPath))
        {
            if (!options.Overwrite)
                throw new ConversionException($"Destination already exists: {options.DecentDbPath}");
            File.Delete(options.DecentDbPath);
            var walPath = options.DecentDbPath + "-wal";
            if (File.Exists(walPath)) File.Delete(walPath);
        }

        var sw = Stopwatch.StartNew();
        var report = new ImportReport
        {
            SqlitePath = options.SqlitePath,
            DecentDbPath = options.DecentDbPath
        };

        await using var sqliteConn = new SqliteConnection($"Data Source={options.SqlitePath};Mode=ReadOnly");
        await sqliteConn.OpenAsync(ct);
        sqliteConn.CreateCommand().Apply(c => { c.CommandText = "PRAGMA foreign_keys=ON"; c.ExecuteNonQuery(); });

        // Phase 1: Analyze
        progress?.Report(new ImportProgress { Phase = ImportPhase.Analyzing, Message = "Reading SQLite schema..." });

        var tables = LoadAllTableSchemas(sqliteConn);
        foreach (var t in tables)
            ValidateSupported(t);

        var ordered = ToposortTables(tables);
        var identCase = options.LowercaseIdentifiers ? "lower" : "preserve";
        var (tableNameMap, columnNameMap) = BuildNameMaps(ordered, identCase);

        report.Tables.AddRange(ordered.Select(t => tableNameMap[t.Name]));
        foreach (var t in ordered)
        {
            foreach (var col in t.Columns)
            {
                if (col.IsUnique && !col.IsPrimaryKey)
                    report.UniqueColumnsAdded.Add($"{tableNameMap[t.Name]}.{columnNameMap[t.Name][col.Name]}");
            }
            report.SkippedIndexes.AddRange(t.SkippedIndexes);
        }

        ct.ThrowIfCancellationRequested();

        // Open DecentDB connection
        var decentConn = new DecentDB.AdoNet.DecentDBConnection($"Data Source={options.DecentDbPath}");
        await decentConn.OpenAsync(ct);

        try
        {
            // Phase 2: Create schema
            progress?.Report(new ImportProgress
            {
                Phase = ImportPhase.CreatingSchema,
                TablesTotal = ordered.Count,
                Message = "Creating tables..."
            });

            await ExecuteNonQueryAsync(decentConn, "BEGIN", ct);
            for (var i = 0; i < ordered.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var createSql = BuildCreateTableSql(ordered[i], tableNameMap, columnNameMap);
                Log.Debug("Creating table: {Sql}", createSql);
                await ExecuteNonQueryAsync(decentConn, createSql, ct);

                progress?.Report(new ImportProgress
                {
                    Phase = ImportPhase.CreatingSchema,
                    TablesCompleted = i + 1,
                    TablesTotal = ordered.Count,
                    Message = $"Created table {tableNameMap[ordered[i].Name]}"
                });
            }
            await ExecuteNonQueryAsync(decentConn, "COMMIT", ct);

            // Phase 3: Copy data
            for (var i = 0; i < ordered.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var table = ordered[i];
                var totalRows = GetTableRowCount(sqliteConn, table.Name);

                await CopyTableDataAsync(
                    sqliteConn, decentConn, table,
                    tableNameMap, columnNameMap,
                    options.CommitBatchSize, totalRows,
                    i, ordered.Count,
                    progress, ct);

                report.RowsCopied[tableNameMap[table.Name]] = totalRows;
            }

            // Phase 4: Create indexes
            var totalIndexes = ordered.Sum(t => t.Indexes.Count);
            progress?.Report(new ImportProgress
            {
                Phase = ImportPhase.CreatingIndexes,
                IndexesTotal = totalIndexes,
                Message = "Creating indexes..."
            });

            var createdCount = 0;
            await ExecuteNonQueryAsync(decentConn, "BEGIN", ct);
            foreach (var table in ordered)
            {
                foreach (var idx in table.Indexes)
                {
                    ct.ThrowIfCancellationRequested();
                    var dstTable = tableNameMap[idx.Table];
                    var dstCol = columnNameMap[idx.Table][idx.Column];
                    var dstIdx = NormalizeIdentifier(idx.Name, identCase);
                    var sql = $"CREATE INDEX {QuoteIdentifier(dstIdx)} ON {QuoteIdentifier(dstTable)}({QuoteIdentifier(dstCol)})";
                    await ExecuteNonQueryAsync(decentConn, sql, ct);
                    report.IndexesCreated.Add(dstIdx);
                    createdCount++;

                    progress?.Report(new ImportProgress
                    {
                        Phase = ImportPhase.CreatingIndexes,
                        IndexesCompleted = createdCount,
                        IndexesTotal = totalIndexes,
                        Message = $"Created index {dstIdx}"
                    });
                }
            }
            await ExecuteNonQueryAsync(decentConn, "COMMIT", ct);

            sw.Stop();
            report.Elapsed = sw.Elapsed;

            progress?.Report(new ImportProgress
            {
                Phase = ImportPhase.Complete,
                TablesCompleted = ordered.Count,
                TablesTotal = ordered.Count,
                IndexesCompleted = totalIndexes,
                IndexesTotal = totalIndexes,
                Message = $"Import complete — {report.Tables.Count} tables, {report.TotalRows:N0} rows, {report.IndexesCreated.Count} indexes"
            });

            return report;
        }
        catch (OperationCanceledException)
        {
            TryRollback(decentConn);
            throw;
        }
        catch
        {
            TryRollback(decentConn);
            throw;
        }
        finally
        {
            await decentConn.CloseAsync();
            decentConn.Dispose();
        }
    }

    #region Schema Reading

    private static List<SqliteTable> LoadAllTableSchemas(SqliteConnection conn)
    {
        var tableNames = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                tableNames.Add(reader.GetString(0));
        }

        return tableNames.Select(name => LoadTableSchema(conn, name)).ToList();
    }

    private static SqliteTable LoadTableSchema(SqliteConnection conn, string tableName)
    {
        // Columns via PRAGMA table_info
        var columns = new List<SqliteColumn>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA table_info({QuoteIdentifier(tableName)})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(1);
                var declaredType = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var notNull = reader.GetInt64(3) != 0;
                var pk = reader.GetInt64(5) > 0;
                columns.Add(new SqliteColumn(name, declaredType, notNull, pk));
            }
        }

        if (columns.Count == 0)
            throw new ConversionException($"Table has no columns: {tableName}");

        // Foreign keys via PRAGMA foreign_key_list
        // Columns: id, seq, table, from, to, on_update, on_delete, match
        // "to" (ordinal 4) can be NULL when FK references PK implicitly
        var foreignKeys = new List<SqliteForeignKey>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA foreign_key_list({QuoteIdentifier(tableName)})";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var toTable = reader.GetString(2);
                var fromCol = reader.GetString(3);
                var toCol = reader.IsDBNull(4) ? null : reader.GetString(4);

                // If to_column is null, the FK targets the primary key of the referenced table.
                // We skip it here since we can't resolve the PK column without querying that table.
                if (toCol is null)
                    continue;

                foreignKeys.Add(new SqliteForeignKey(fromCol, toTable, toCol));
            }
        }

        // Indexes via PRAGMA index_list + index_info
        var colPos = columns.Select((c, i) => (c.Name, i)).ToDictionary(x => x.Name, x => x.i);
        var indexes = new List<SqliteIndex>();
        var skipped = new List<SkippedIndex>();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"PRAGMA index_list({QuoteIdentifier(tableName)})";
            using var reader = cmd.ExecuteReader();
            var indexRows = new List<(string Name, bool Unique, string Origin)>();
            while (reader.Read())
            {
                indexRows.Add((
                    reader.GetString(1),
                    reader.GetInt64(2) != 0,
                    reader.IsDBNull(3) ? "" : reader.GetString(3)
                ));
            }

            foreach (var (idxName, unique, origin) in indexRows)
            {
                // Skip implicit PK indexes
                if (origin.Equals("pk", StringComparison.OrdinalIgnoreCase))
                    continue;

                var indexCols = new List<string>();
                using (var idxCmd = conn.CreateCommand())
                {
                    idxCmd.CommandText = $"PRAGMA index_info({QuoteIdentifier(idxName)})";
                    using var idxReader = idxCmd.ExecuteReader();
                    while (idxReader.Read())
                    {
                        // Ordinal 2 (column name) can be NULL for expression indexes
                        if (idxReader.IsDBNull(2))
                        {
                            skipped.Add(new SkippedIndex(idxName, tableName, "Expression index not supported"));
                            indexCols.Clear();
                            break;
                        }
                        indexCols.Add(idxReader.GetString(2));
                    }
                }

                if (indexCols.Count == 0)
                    continue;

                if (indexCols.Count != 1)
                {
                    var reason = unique
                        ? "Composite UNIQUE constraint/index not supported (single-column only)"
                        : "Composite index not imported (single-column only)";
                    skipped.Add(new SkippedIndex(idxName, tableName, reason));
                    continue;
                }

                var colName = indexCols[0];

                // Unique single-column indexes become column-level UNIQUE constraints
                if (unique && colPos.ContainsKey(colName))
                {
                    var idx = colPos[colName];
                    columns[idx] = columns[idx] with { IsUnique = true };
                    continue;
                }

                indexes.Add(new SqliteIndex(idxName, tableName, colName, false));
            }
        }

        return new SqliteTable(tableName, columns, foreignKeys, indexes, skipped);
    }

    #endregion

    #region Validation

    internal static void ValidateSupported(SqliteTable table)
    {
        var fkByFrom = table.ForeignKeys
            .GroupBy(fk => fk.FromColumn)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in fkByFrom)
        {
            throw new ConversionException(
                $"Multiple foreign key targets from one column not supported: {table.Name}.{group.Key}");
        }
    }

    #endregion

    #region Topological Sort

    internal static List<SqliteTable> ToposortTables(List<SqliteTable> tables)
    {
        var byName = tables.ToDictionary(t => t.Name);
        var deps = tables.ToDictionary(t => t.Name, _ => new HashSet<string>());
        var rev = tables.ToDictionary(t => t.Name, _ => new HashSet<string>());

        foreach (var t in tables)
        {
            foreach (var fk in t.ForeignKeys)
            {
                if (!byName.ContainsKey(fk.ToTable))
                {
                    throw new ConversionException(
                        $"Foreign key references missing table: {t.Name}.{fk.FromColumn} -> {fk.ToTable}({fk.ToColumn})");
                }

                // Self-referencing FKs don't create ordering dependencies
                if (fk.ToTable != t.Name)
                {
                    deps[t.Name].Add(fk.ToTable);
                    rev[fk.ToTable].Add(t.Name);
                }
            }
        }

        var indeg = deps.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
        var queue = new Queue<string>(indeg.Where(kv => kv.Value == 0).Select(kv => kv.Key));
        var result = new List<string>();

        while (queue.Count > 0)
        {
            var n = queue.Dequeue();
            result.Add(n);
            foreach (var child in rev[n])
            {
                indeg[child]--;
                if (indeg[child] == 0)
                    queue.Enqueue(child);
            }
        }

        if (result.Count != tables.Count)
        {
            var cycle = indeg.Where(kv => kv.Value > 0).Select(kv => kv.Key).OrderBy(x => x);
            throw new ConversionException($"Foreign key cycle detected among tables: {string.Join(", ", cycle)}");
        }

        return result.Select(name => byName[name]).ToList();
    }

    #endregion

    #region Identifier Mapping

    internal static string NormalizeIdentifier(string name, string identifierCase)
    {
        return identifierCase switch
        {
            "lower" => name.ToLowerInvariant(),
            "preserve" => name,
            _ => throw new ArgumentException($"Unknown identifier case: {identifierCase}")
        };
    }

    internal static (Dictionary<string, string> TableMap, Dictionary<string, Dictionary<string, string>> ColumnMap)
        BuildNameMaps(List<SqliteTable> tables, string identifierCase)
    {
        var tableMap = new Dictionary<string, string>();
        var usedTables = new Dictionary<string, string>();

        foreach (var t in tables)
        {
            var dst = NormalizeIdentifier(t.Name, identifierCase);
            if (usedTables.TryGetValue(dst, out var existing) && existing != t.Name)
            {
                throw new ConversionException(
                    $"Table name collision after normalization: '{t.Name}' and '{existing}' -> '{dst}'");
            }
            usedTables[dst] = t.Name;
            tableMap[t.Name] = dst;
        }

        var columnMap = new Dictionary<string, Dictionary<string, string>>();
        foreach (var t in tables)
        {
            var usedCols = new Dictionary<string, string>();
            var per = new Dictionary<string, string>();
            foreach (var c in t.Columns)
            {
                var dst = NormalizeIdentifier(c.Name, identifierCase);
                if (usedCols.TryGetValue(dst, out var existing) && existing != c.Name)
                {
                    throw new ConversionException(
                        $"Column name collision after normalization: '{t.Name}.{c.Name}' and '{t.Name}.{existing}' -> '{dst}'");
                }
                usedCols[dst] = c.Name;
                per[c.Name] = dst;
            }
            columnMap[t.Name] = per;
        }

        return (tableMap, columnMap);
    }

    #endregion

    #region Type Mapping

    /// <summary>
    /// Maps a SQLite declared type to the closest DecentDB type.
    /// Exact port of _map_declared_type_to_decentdb from the Python script.
    /// </summary>
    internal static string MapDeclaredTypeToDecentDb(string declaredType)
    {
        var t = (declaredType ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(t))
            return "TEXT";

        if (t.Contains("BOOL"))
            return "BOOL";
        if (t.Contains("INT"))
            return "INT64";
        if (t.Contains("REAL") || t.Contains("FLOA") || t.Contains("DOUB"))
            return "FLOAT64";
        if (t.Contains("BLOB"))
            return "BLOB";
        if (t.Contains("UUID"))
            return "UUID";
        if (t.Contains("DECIMAL") || t.Contains("NUMERIC"))
        {
            var mapped = t.Replace("NUMERIC", "DECIMAL");
            if (mapped.Contains('('))
                return mapped;
            return "DECIMAL(18,6)";
        }
        if (t.Contains("CHAR") || t.Contains("CLOB") || t.Contains("TEXT") || t.Contains("VARCHAR"))
            return "TEXT";

        // SQLite allows arbitrary type names; default to TEXT
        return "TEXT";
    }

    #endregion

    #region SQL Generation

    internal static string QuoteIdentifier(string name)
    {
        return "\"" + name.Replace("\"", "\"\"") + "\"";
    }

    private static string BuildCreateTableSql(
        SqliteTable table,
        Dictionary<string, string> tableNameMap,
        Dictionary<string, Dictionary<string, string>> columnNameMap)
    {
        var fkMap = table.ForeignKeys.ToDictionary(fk => fk.FromColumn);
        var dstTable = tableNameMap[table.Name];
        var pkCols = table.Columns.Where(c => c.IsPrimaryKey).ToList();
        var compositePk = pkCols.Count > 1;
        var colDefs = new List<string>();

        foreach (var col in table.Columns)
        {
            var dstCol = columnNameMap[table.Name][col.Name];
            var parts = new List<string> { QuoteIdentifier(dstCol), MapDeclaredTypeToDecentDb(col.DeclaredType) };

            if (col.IsPrimaryKey && !compositePk)
            {
                parts.Add("PRIMARY KEY");
            }
            else if (col.IsPrimaryKey && compositePk)
            {
                parts.Add("NOT NULL");
            }
            else
            {
                if (col.IsUnique)
                    parts.Add("UNIQUE");
                if (col.NotNull)
                    parts.Add("NOT NULL");
            }

            if (fkMap.TryGetValue(col.Name, out var fk))
            {
                var refTable = tableNameMap[fk.ToTable];
                var refCol = columnNameMap[fk.ToTable][fk.ToColumn];
                parts.Add($"REFERENCES {QuoteIdentifier(refTable)}({QuoteIdentifier(refCol)})");
            }

            colDefs.Add(string.Join(" ", parts));
        }

        if (compositePk)
        {
            var pkColNames = string.Join(", ", pkCols.Select(c =>
                QuoteIdentifier(columnNameMap[table.Name][c.Name])));
            colDefs.Add($"PRIMARY KEY ({pkColNames})");
        }

        return $"CREATE TABLE {QuoteIdentifier(dstTable)} ({string.Join(", ", colDefs)})";
    }

    #endregion

    #region Data Copy

    private static async Task CopyTableDataAsync(
        SqliteConnection sqliteConn,
        DbConnection decentConn,
        SqliteTable table,
        Dictionary<string, string> tableNameMap,
        Dictionary<string, Dictionary<string, string>> columnNameMap,
        int commitBatchSize,
        long totalRows,
        int tableIndex,
        int totalTables,
        IProgress<ImportProgress>? progress,
        CancellationToken ct)
    {
        var dstTable = tableNameMap[table.Name];
        var dstCols = table.Columns.Select(c => columnNameMap[table.Name][c.Name]).ToList();
        var colsSql = string.Join(", ", dstCols.Select(QuoteIdentifier));

        // Build placeholders with CAST for DECIMAL/UUID types
        var placeholders = new List<string>();
        for (var i = 0; i < table.Columns.Count; i++)
        {
            var dtype = MapDeclaredTypeToDecentDb(table.Columns[i].DeclaredType);
            if (dtype.StartsWith("DECIMAL") || dtype.StartsWith("NUMERIC"))
                placeholders.Add($"CAST(@p{i} AS {dtype})");
            else if (dtype == "UUID")
                placeholders.Add($"CAST(@p{i} AS UUID)");
            else
                placeholders.Add($"@p{i}");
        }

        var insertSql = $"INSERT INTO {QuoteIdentifier(dstTable)} ({colsSql}) VALUES ({string.Join(", ", placeholders)})";

        progress?.Report(new ImportProgress
        {
            Phase = ImportPhase.CopyingData,
            CurrentTable = dstTable,
            RowsCompleted = 0,
            RowsTotal = totalRows,
            TablesCompleted = tableIndex,
            TablesTotal = totalTables,
            Message = $"Copying {dstTable}..."
        });

        long rowCount = 0;
        var inTx = false;

        if (commitBatchSize > 0)
        {
            await ExecuteNonQueryAsync(decentConn, "BEGIN", ct);
            inTx = true;
        }

        // Streaming read from SQLite
        var selectCols = string.Join(", ", table.Columns.Select(c => QuoteIdentifier(c.Name)));
        using var selectCmd = sqliteConn.CreateCommand();
        selectCmd.CommandText = $"SELECT {selectCols} FROM {QuoteIdentifier(table.Name)}";
        await using var reader = await selectCmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
        {
            ct.ThrowIfCancellationRequested();

            using var insertCmd = decentConn.CreateCommand();
            insertCmd.CommandText = insertSql;

            for (var i = 0; i < table.Columns.Count; i++)
            {
                var param = insertCmd.CreateParameter();
                param.ParameterName = $"@p{i}";
                param.Value = AdaptValue(table.Columns[i], reader.GetValue(i));
                insertCmd.Parameters.Add(param);
            }

            await insertCmd.ExecuteNonQueryAsync(ct);
            rowCount++;

            if (inTx && commitBatchSize > 0 && rowCount % commitBatchSize == 0)
            {
                await ExecuteNonQueryAsync(decentConn, "COMMIT", ct);
                await ExecuteNonQueryAsync(decentConn, "BEGIN", ct);
            }

            // Report progress every 200 rows to keep UI responsive without flooding
            if (rowCount % 200 == 0 || rowCount == totalRows)
            {
                progress?.Report(new ImportProgress
                {
                    Phase = ImportPhase.CopyingData,
                    CurrentTable = dstTable,
                    RowsCompleted = rowCount,
                    RowsTotal = totalRows,
                    TablesCompleted = tableIndex,
                    TablesTotal = totalTables,
                    Message = $"Copying {dstTable}: {rowCount:N0} / {totalRows:N0} rows"
                });
            }
        }

        if (inTx)
        {
            await ExecuteNonQueryAsync(decentConn, "COMMIT", ct);
        }

        progress?.Report(new ImportProgress
        {
            Phase = ImportPhase.CopyingData,
            CurrentTable = dstTable,
            RowsCompleted = totalRows,
            RowsTotal = totalRows,
            TablesCompleted = tableIndex + 1,
            TablesTotal = totalTables,
            Message = $"Copied {dstTable}: {rowCount:N0} rows"
        });
    }

    /// <summary>
    /// Adapt a SQLite value for DecentDB insertion. Primarily handles bool mapping.
    /// </summary>
    internal static object AdaptValue(SqliteColumn column, object value)
    {
        if (value is DBNull)
            return DBNull.Value;

        var decentType = MapDeclaredTypeToDecentDb(column.DeclaredType);
        if (decentType == "BOOL")
        {
            if (value is bool b)
                return b;
            if (value is long l && l is 0 or 1)
                return l == 1;
            if (value is int i && i is 0 or 1)
                return i == 1;
        }

        return value;
    }

    #endregion

    #region Helpers

    private static long GetTableRowCount(SqliteConnection conn, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {QuoteIdentifier(tableName)}";
        return (long)cmd.ExecuteScalar()!;
    }

    private static async Task ExecuteNonQueryAsync(DbConnection conn, string sql, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void TryRollback(DbConnection conn)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "ROLLBACK";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort rollback
        }
    }

    #endregion
}

/// <summary>
/// Extension to allow inline command configuration.
/// </summary>
internal static class DbCommandExtensions
{
    public static void Apply(this DbCommand cmd, Action<DbCommand> configure)
    {
        configure(cmd);
    }
}
