using System.Data.Common;
using System.Diagnostics;
using System.Text;
using Serilog;
using ZstdSharp;

namespace MehSql.Core.Import;

/// <summary>
/// Import source for MySQL Shell dump directories.
/// Reads JSON metadata, SQL schema files, and zstd-compressed TSV data chunks.
/// </summary>
public sealed class MysqlShellDumpImportSource : IImportSource
{
    /// <inheritdoc />
    public ImportFormat Format => ImportFormat.MysqlShellDump;

    /// <inheritdoc />
    public async Task<GenericAnalysisResult> AnalyzeAsync(string sourcePath, CancellationToken ct = default)
    {
        if (!Directory.Exists(sourcePath))
            throw new DirectoryNotFoundException($"MySQL Shell dump directory not found: {sourcePath}");

        if (!File.Exists(Path.Combine(sourcePath, "@.json")))
            throw new FileNotFoundException("Not a MySQL Shell dump directory (missing @.json).", sourcePath);

        ct.ThrowIfCancellationRequested();

        var schema = await Task.Run(() => MysqlShellDumpParser.ParseDumpDirectory(sourcePath), ct);

        // Count rows by streaming through all .tsv.zst chunks (count lines only)
        var rowCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in schema.Tables)
        {
            ct.ThrowIfCancellationRequested();

            var metaKey = $"{schema.DefaultSchema}.{table.Name}";
            if (!schema.TableMetas.TryGetValue(metaKey, out var meta))
                continue;

            var chunks = MysqlShellDumpParser.GetDataChunkFiles(sourcePath, meta.Schema, table.Name);
            long count = 0;

            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                count += await CountLinesInZstFileAsync(chunk, ct);
            }

            rowCounts[table.Name] = count;
        }

        return new GenericAnalysisResult
        {
            SourcePath = sourcePath,
            Format = ImportFormat.MysqlShellDump,
            TableNames = schema.Tables.Select(t => t.Name).ToList(),
            RowCounts = rowCounts,
            Warnings = [.. schema.Warnings]
        };
    }

    /// <inheritdoc />
    public async Task<ImportReport> ImportAsync(
        GenericImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(options.SourcePath))
            throw new DirectoryNotFoundException($"MySQL Shell dump directory not found: {options.SourcePath}");

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
            Format = ImportFormat.MysqlShellDump
        };

        // Phase 1: Analyze
        progress?.Report(new ImportProgress
        {
            Phase = ImportPhase.Analyzing,
            Message = "Parsing MySQL Shell dump schema..."
        });

        var schema = await Task.Run(() => MysqlShellDumpParser.ParseDumpDirectory(options.SourcePath), ct);
        report.Warnings.AddRange(schema.Warnings);
        report.Tables.AddRange(schema.Tables.Select(t => t.Name));

        // Count rows for progress tracking
        var rowCounts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in schema.Tables)
        {
            ct.ThrowIfCancellationRequested();
            var metaKey = $"{schema.DefaultSchema}.{table.Name}";
            if (!schema.TableMetas.TryGetValue(metaKey, out var meta))
                continue;

            var chunks = MysqlShellDumpParser.GetDataChunkFiles(options.SourcePath, meta.Schema, table.Name);
            long count = 0;
            foreach (var chunk in chunks)
            {
                ct.ThrowIfCancellationRequested();
                count += await CountLinesInZstFileAsync(chunk, ct);
            }
            rowCounts[table.Name] = count;
        }

        long totalRows = rowCounts.Values.Sum();

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
                var createSql = MysqlDumpImportSource.BuildCreateTableSql(table, options.LowercaseIdentifiers);
                Log.Debug("Creating table: {Sql}", createSql);
                await ExecuteNonQueryAsync(decentConn, createSql, ct);

                progress?.Report(new ImportProgress
                {
                    Phase = ImportPhase.CreatingSchema,
                    TablesCompleted = i + 1,
                    TablesTotal = schema.Tables.Count,
                    Message = $"Created table {MysqlDumpImportSource.MaybeToLower(table.Name, options.LowercaseIdentifiers)}"
                });
            }
            await ExecuteNonQueryAsync(decentConn, "COMMIT", ct);

            // Phase 3: Copy data from zstd-compressed TSV chunks
            progress?.Report(new ImportProgress
            {
                Phase = ImportPhase.CopyingData,
                TablesTotal = schema.Tables.Count,
                Message = "Importing data..."
            });

            long globalRowsCompleted = 0;
            var tablesCompleted = 0;

            foreach (var table in schema.Tables)
            {
                ct.ThrowIfCancellationRequested();

                var metaKey = $"{schema.DefaultSchema}.{table.Name}";
                if (!schema.TableMetas.TryGetValue(metaKey, out var meta))
                {
                    report.Warnings.Add($"No metadata found for table {table.Name}, skipping data import");
                    continue;
                }

                var chunks = MysqlShellDumpParser.GetDataChunkFiles(options.SourcePath, meta.Schema, table.Name);
                if (chunks.Count == 0)
                {
                    tablesCompleted++;
                    report.RowsCopied[MysqlDumpImportSource.MaybeToLower(table.Name, options.LowercaseIdentifiers)] = 0;
                    continue;
                }

                var dstTable = MysqlDumpImportSource.MaybeToLower(table.Name, options.LowercaseIdentifiers);
                var dstCols = table.Columns.Select(c =>
                    MysqlDumpImportSource.MaybeToLower(c.Name, options.LowercaseIdentifiers)).ToList();
                var colTypes = table.Columns.Select(c => c.DecentDbType).ToList();

                var colsSql = string.Join(", ", dstCols.Select(SqliteImportService.QuoteIdentifier));
                var placeholders = BuildPlaceholders(colTypes);
                var insertSql = $"INSERT INTO {SqliteImportService.QuoteIdentifier(dstTable)} ({colsSql}) VALUES ({string.Join(", ", placeholders)})";

                long tableRowCount = 0;
                var batchSize = options.CommitBatchSize > 0 ? options.CommitBatchSize : 5_000;

                foreach (var chunkPath in chunks)
                {
                    ct.ThrowIfCancellationRequested();

                    await Task.Run(() =>
                    {
                        using var fileStream = File.OpenRead(chunkPath);
                        using var zstdStream = new DecompressionStream(fileStream);
                        using var reader = new StreamReader(zstdStream, Encoding.UTF8);

                        ExecuteNonQuery(decentConn, "BEGIN");

                        string? line;
                        while ((line = reader.ReadLine()) is not null)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (string.IsNullOrEmpty(line))
                                continue;

                            var values = MysqlShellDumpParser.ParseTsvLine(line, table.Columns, meta.FieldsEscapedBy);

                            if (values.Length != table.Columns.Count)
                            {
                                report.Warnings.Add(
                                    $"Row column count mismatch in {table.Name}: expected {table.Columns.Count}, got {values.Length}");
                                continue;
                            }

                            using var cmd = decentConn.CreateCommand();
                            cmd.CommandText = insertSql;
                            for (var pi = 0; pi < values.Length; pi++)
                            {
                                var param = cmd.CreateParameter();
                                param.ParameterName = $"@p{pi}";
                                param.Value = values[pi] ?? DBNull.Value;
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
                    }, ct);
                }

                report.RowsCopied[dstTable] = tableRowCount;
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

            // Phase 4: Create indexes (single-column only)
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
                var dstTable = MysqlDumpImportSource.MaybeToLower(idx.Table, options.LowercaseIdentifiers);
                var dstCol = MysqlDumpImportSource.MaybeToLower(idx.Columns[0], options.LowercaseIdentifiers);
                var dstIdx = MysqlDumpImportSource.MaybeToLower(idx.Name, options.LowercaseIdentifiers);

                if (idx.IsUnique)
                {
                    report.UniqueColumnsAdded.Add($"{dstTable}.{dstCol}");
                }

                var sql = idx.IsUnique
                    ? $"CREATE UNIQUE INDEX {SqliteImportService.QuoteIdentifier(dstIdx)} ON {SqliteImportService.QuoteIdentifier(dstTable)}({SqliteImportService.QuoteIdentifier(dstCol)})"
                    : $"CREATE INDEX {SqliteImportService.QuoteIdentifier(dstIdx)} ON {SqliteImportService.QuoteIdentifier(dstTable)}({SqliteImportService.QuoteIdentifier(dstCol)})";

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

    #region Helpers

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

    /// <summary>
    /// Count lines in a zstd-compressed file without storing data.
    /// </summary>
    private static async Task<long> CountLinesInZstFileAsync(string path, CancellationToken ct)
    {
        long count = 0;

        await Task.Run(() =>
        {
            using var fileStream = File.OpenRead(path);
            using var zstdStream = new DecompressionStream(fileStream);
            using var reader = new StreamReader(zstdStream, Encoding.UTF8);

            while (reader.ReadLine() is not null)
            {
                ct.ThrowIfCancellationRequested();
                count++;
            }
        }, ct);

        return count;
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
