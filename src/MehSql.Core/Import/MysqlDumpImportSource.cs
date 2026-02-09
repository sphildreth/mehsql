using System.Data.Common;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Serilog;

namespace MehSql.Core.Import;

/// <summary>
/// Import source for MySQL <c>mysqldump</c> plain SQL dump files.
/// Implements the two-pass approach: first parse schema and count rows, then stream INSERT data.
/// </summary>
public sealed class MysqlDumpImportSource : IImportSource
{
    private static readonly Regex InsertIntoRx = new(
        @"^INSERT\s+INTO\s+`?(\w+)`?\s+VALUES\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <inheritdoc />
    public ImportFormat Format => ImportFormat.MysqlDump;

    /// <inheritdoc />
    public async Task<GenericAnalysisResult> AnalyzeAsync(string sourcePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("mysqldump file not found.", sourcePath);

        ct.ThrowIfCancellationRequested();

        MysqlDumpSchema schema;
        using (var reader = new StreamReader(sourcePath, Encoding.UTF8))
        {
            schema = await Task.Run(() => MysqlDumpParser.ParseSchema(reader), ct);
        }

        return new GenericAnalysisResult
        {
            SourcePath = sourcePath,
            Format = ImportFormat.MysqlDump,
            TableNames = schema.Tables.Select(t => t.Name).ToList(),
            RowCounts = new Dictionary<string, long>(schema.RowCounts),
            Warnings = [.. schema.Warnings]
        };
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportAsync(
        GenericImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!File.Exists(options.SourcePath))
            throw new FileNotFoundException("mysqldump file not found.", options.SourcePath);

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
            SourcePath = options.SourcePath,
            DecentDbPath = options.DecentDbPath,
            Format = ImportFormat.MysqlDump
        };

        // Phase 1: Analyze — first pass reads schema and counts INSERT rows
        progress?.Report(new ImportProgress
        {
            Phase = ImportPhase.Analyzing,
            Message = "Parsing mysqldump schema..."
        });

        MysqlDumpSchema schema;
        using (var reader = new StreamReader(options.SourcePath, Encoding.UTF8))
        {
            schema = await Task.Run(() => MysqlDumpParser.ParseSchema(reader), ct);
        }

        report.Warnings.AddRange(schema.Warnings);
        report.Tables.AddRange(schema.Tables.Select(t => t.Name));

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
                TablesTotal = schema.Tables.Count,
                Message = "Creating tables..."
            });

            await ExecuteNonQueryAsync(decentConn, "BEGIN", ct);
            for (var i = 0; i < schema.Tables.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var table = schema.Tables[i];
                var createSql = BuildCreateTableSql(table, options.LowercaseIdentifiers);
                Log.Debug("Creating table: {Sql}", createSql);
                await ExecuteNonQueryAsync(decentConn, createSql, ct);

                progress?.Report(new ImportProgress
                {
                    Phase = ImportPhase.CreatingSchema,
                    TablesCompleted = i + 1,
                    TablesTotal = schema.Tables.Count,
                    Message = $"Created table {MaybeToLower(table.Name, options.LowercaseIdentifiers)}"
                });
            }
            await ExecuteNonQueryAsync(decentConn, "COMMIT", ct);

            // Phase 3: Copy data — second pass streams INSERT statements
            progress?.Report(new ImportProgress
            {
                Phase = ImportPhase.CopyingData,
                TablesTotal = schema.Tables.Count,
                Message = "Importing data..."
            });

            var tableColumnMap = schema.Tables.ToDictionary(
                t => t.Name,
                t => t.Columns,
                StringComparer.OrdinalIgnoreCase);

            long totalRows = schema.RowCounts.Values.Sum();
            long globalRowsCompleted = 0;
            var tablesCompleted = 0;

            using (var reader = new StreamReader(options.SourcePath, Encoding.UTF8))
            {
                await Task.Run(() =>
                {
                    string? line;
                    var continuationBuilder = new StringBuilder();

                    while ((line = reader.ReadLine()) is not null)
                    {
                        ct.ThrowIfCancellationRequested();

                        var trimmed = line.TrimStart();

                        // Strip conditional comments
                        if (trimmed.StartsWith("/*!"))
                            continue;

                        var insertMatch = InsertIntoRx.Match(trimmed);
                        if (!insertMatch.Success)
                            continue;

                        var tableName = insertMatch.Groups[1].Value;
                        if (!tableColumnMap.TryGetValue(tableName, out var schemaColumns))
                            continue;

                        // Build the full values part (handle rare multi-line INSERTs)
                        var valuesPart = trimmed[insertMatch.Length..];
                        if (!valuesPart.TrimEnd().EndsWith(';'))
                        {
                            continuationBuilder.Clear();
                            continuationBuilder.Append(valuesPart);
                            while ((line = reader.ReadLine()) is not null)
                            {
                                continuationBuilder.Append(line);
                                if (line.TrimEnd().EndsWith(';'))
                                    break;
                            }
                            valuesPart = continuationBuilder.ToString();
                        }

                        // Remove trailing semicolon
                        valuesPart = valuesPart.TrimEnd();
                        if (valuesPart.EndsWith(';'))
                            valuesPart = valuesPart[..^1];

                        var dstTable = MaybeToLower(tableName, options.LowercaseIdentifiers);
                        var dstCols = schemaColumns.Select(c => MaybeToLower(c.Name, options.LowercaseIdentifiers)).ToList();
                        var colTypes = schemaColumns.Select(c => c.DecentDbType).ToList();

                        var colsSql = string.Join(", ", dstCols.Select(QuoteIdentifier));
                        var placeholders = BuildPlaceholders(colTypes);
                        var insertSql = $"INSERT INTO {QuoteIdentifier(dstTable)} ({colsSql}) VALUES ({string.Join(", ", placeholders)})";

                        long tableRowCount = 0;
                        var batchSize = options.CommitBatchSize > 0 ? options.CommitBatchSize : 5_000;

                        ExecuteNonQuery(decentConn, "BEGIN");

                        foreach (var row in MysqlDumpParser.ParseInsertValues(valuesPart, schemaColumns))
                        {
                            ct.ThrowIfCancellationRequested();

                            if (row.Length != schemaColumns.Count)
                            {
                                report.Warnings.Add(
                                    $"Row column count mismatch in {tableName}: expected {schemaColumns.Count}, got {row.Length}");
                                continue;
                            }

                            using var cmd = decentConn.CreateCommand();
                            cmd.CommandText = insertSql;
                            for (var pi = 0; pi < row.Length; pi++)
                            {
                                var param = cmd.CreateParameter();
                                param.ParameterName = $"@p{pi}";
                                param.Value = row[pi] ?? DBNull.Value;
                                cmd.Parameters.Add(param);
                            }
                            cmd.ExecuteNonQuery();

                            tableRowCount++;
                            globalRowsCompleted++;

                            if (tableRowCount % batchSize == 0)
                            {
                                ExecuteNonQuery(decentConn, "COMMIT");
                                ExecuteNonQuery(decentConn, "BEGIN");
                            }

                            if (tableRowCount % 200 == 0)
                            {
                                progress?.Report(new ImportProgress
                                {
                                    Phase = ImportPhase.CopyingData,
                                    CurrentTable = dstTable,
                                    RowsCompleted = globalRowsCompleted,
                                    RowsTotal = totalRows,
                                    TablesCompleted = tablesCompleted,
                                    TablesTotal = schema.Tables.Count,
                                    Message = $"Copying {dstTable}: {tableRowCount:N0} rows"
                                });
                            }
                        }

                        ExecuteNonQuery(decentConn, "COMMIT");

                        // Accumulate rows for tables with multiple INSERT statements
                        report.RowsCopied.TryGetValue(dstTable, out var existingRows);
                        report.RowsCopied[dstTable] = existingRows + tableRowCount;

                        if (tableRowCount > 0)
                        {
                            tablesCompleted++;

                            progress?.Report(new ImportProgress
                            {
                                Phase = ImportPhase.CopyingData,
                                CurrentTable = dstTable,
                                RowsCompleted = globalRowsCompleted,
                                RowsTotal = totalRows,
                                TablesCompleted = tablesCompleted,
                                TablesTotal = schema.Tables.Count,
                                Message = $"Copied {dstTable}: {tableRowCount:N0} rows"
                            });
                        }
                    }
                }, ct);
            }

            // Phase 4: Create indexes (single-column only, skip composites with warning)
            var allIndexes = schema.Tables
                .SelectMany(t => t.Indexes.Select(idx => idx with { Table = t.Name }))
                .ToList();

            var singleColIndexes = new List<MysqlIndex>();
            var skippedIndexes = new List<SkippedIndex>();

            foreach (var idx in allIndexes)
            {
                if (idx.Columns.Count == 1)
                {
                    singleColIndexes.Add(idx);
                }
                else
                {
                    skippedIndexes.Add(new SkippedIndex(idx.Name, idx.Table,
                        "Composite index not imported (single-column only)"));
                }
            }

            report.SkippedIndexes.AddRange(skippedIndexes);

            progress?.Report(new ImportProgress
            {
                Phase = ImportPhase.CreatingIndexes,
                IndexesTotal = singleColIndexes.Count,
                Message = "Creating indexes..."
            });

            await ExecuteNonQueryAsync(decentConn, "BEGIN", ct);
            for (var i = 0; i < singleColIndexes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var idx = singleColIndexes[i];
                var dstTable = MaybeToLower(idx.Table, options.LowercaseIdentifiers);
                var dstCol = MaybeToLower(idx.Columns[0], options.LowercaseIdentifiers);
                var dstIdx = MaybeToLower(idx.Name, options.LowercaseIdentifiers);

                if (idx.IsUnique)
                {
                    report.UniqueColumnsAdded.Add($"{dstTable}.{dstCol}");
                }

                var sql = idx.IsUnique
                    ? $"CREATE UNIQUE INDEX {QuoteIdentifier(dstIdx)} ON {QuoteIdentifier(dstTable)}({QuoteIdentifier(dstCol)})"
                    : $"CREATE INDEX {QuoteIdentifier(dstIdx)} ON {QuoteIdentifier(dstTable)}({QuoteIdentifier(dstCol)})";

                await ExecuteNonQueryAsync(decentConn, sql, ct);
                report.IndexesCreated.Add(dstIdx);

                progress?.Report(new ImportProgress
                {
                    Phase = ImportPhase.CreatingIndexes,
                    IndexesCompleted = i + 1,
                    IndexesTotal = singleColIndexes.Count,
                    Message = $"Created index {dstIdx}"
                });
            }
            await ExecuteNonQueryAsync(decentConn, "COMMIT", ct);

            sw.Stop();
            report.Elapsed = sw.Elapsed;

            progress?.Report(new ImportProgress
            {
                Phase = ImportPhase.Complete,
                TablesCompleted = schema.Tables.Count,
                TablesTotal = schema.Tables.Count,
                IndexesCompleted = singleColIndexes.Count,
                IndexesTotal = singleColIndexes.Count,
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

    #region SQL Generation

    internal static string BuildCreateTableSql(MysqlTable table, bool lowercaseIdentifiers)
    {
        var dstTable = MaybeToLower(table.Name, lowercaseIdentifiers);
        var pkCols = table.Columns.Where(c => c.IsPrimaryKey).ToList();
        var compositePk = pkCols.Count > 1;
        var colDefs = new List<string>();

        foreach (var col in table.Columns)
        {
            var dstCol = MaybeToLower(col.Name, lowercaseIdentifiers);
            var parts = new List<string> { QuoteIdentifier(dstCol), col.DecentDbType };

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

            colDefs.Add(string.Join(" ", parts));
        }

        if (compositePk)
        {
            var pkColNames = string.Join(", ", pkCols.Select(c =>
                QuoteIdentifier(MaybeToLower(c.Name, lowercaseIdentifiers))));
            colDefs.Add($"PRIMARY KEY ({pkColNames})");
        }

        return $"CREATE TABLE {QuoteIdentifier(dstTable)} ({string.Join(", ", colDefs)})";
    }

    private static List<string> BuildPlaceholders(List<string> colTypes)
    {
        var placeholders = new List<string>();
        for (var i = 0; i < colTypes.Count; i++)
        {
            var dtype = colTypes[i];
            if (dtype.StartsWith("DECIMAL") || dtype.StartsWith("NUMERIC"))
                placeholders.Add($"CAST(@p{i} AS {dtype})");
            else if (dtype == "UUID")
                placeholders.Add($"CAST(@p{i} AS UUID)");
            else
                placeholders.Add($"@p{i}");
        }
        return placeholders;
    }

    #endregion

    #region Helpers

    private static string QuoteIdentifier(string name)
    {
        return SqliteImportService.QuoteIdentifier(name);
    }

    internal static string MaybeToLower(string name, bool lowercase)
    {
        return lowercase ? name.ToLowerInvariant() : name;
    }

    private static async Task ExecuteNonQueryAsync(DbConnection conn, string sql, CancellationToken ct)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static void ExecuteNonQuery(DbConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
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
