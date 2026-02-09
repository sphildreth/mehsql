using System.Data.Common;
using System.Diagnostics;
using System.Text;
using Serilog;

namespace MehSql.Core.Import;

/// <summary>
/// Import source for PostgreSQL <c>pg_dump --format=plain</c> SQL dump files.
/// Implements the two-pass approach: first parse schema, then stream COPY data.
/// </summary>
public sealed class PgDumpImportSource : IImportSource
{
    /// <inheritdoc />
    public ImportFormat Format => ImportFormat.PgDump;

    /// <inheritdoc />
    public async Task<GenericAnalysisResult> AnalyzeAsync(string sourcePath, CancellationToken ct = default)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("pg_dump file not found.", sourcePath);

        ct.ThrowIfCancellationRequested();

        PgDumpSchema schema;
        using (var reader = new StreamReader(sourcePath, Encoding.UTF8))
        {
            schema = await Task.Run(() => PgDumpParser.ParseSchema(reader), ct);
        }

        return new GenericAnalysisResult
        {
            SourcePath = sourcePath,
            Format = ImportFormat.PgDump,
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
            throw new FileNotFoundException("pg_dump file not found.", options.SourcePath);

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
            Format = ImportFormat.PgDump
        };

        // Phase 1: Analyze — first pass reads schema and counts COPY rows
        progress?.Report(new ImportProgress
        {
            Phase = ImportPhase.Analyzing,
            Message = "Parsing pg_dump schema..."
        });

        PgDumpSchema schema;
        using (var reader = new StreamReader(options.SourcePath, Encoding.UTF8))
        {
            schema = await Task.Run(() => PgDumpParser.ParseSchema(reader), ct);
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

            // Phase 3: Copy data — second pass streams COPY blocks
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
                    while ((line = reader.ReadLine()) is not null)
                    {
                        ct.ThrowIfCancellationRequested();

                        var copyMatch = CopyBlockPattern.Match(line.TrimStart());
                        if (!copyMatch.Success)
                            continue;

                        var tableName = PgDumpParser.NormalizeIdentifier(copyMatch.Groups[1].Value);
                        var copyColNames = PgDumpParser.ParseColumnList(copyMatch.Groups[2].Value);

                        if (!tableColumnMap.TryGetValue(tableName, out var schemaColumns))
                        {
                            // Skip data for unknown tables
                            SkipCopyData(reader);
                            continue;
                        }

                        var dstTable = MaybeToLower(tableName, options.LowercaseIdentifiers);
                        var dstCols = copyColNames.Select(c => MaybeToLower(c, options.LowercaseIdentifiers)).ToList();

                        // Build column type map for value conversion
                        var colTypeMap = schemaColumns.ToDictionary(
                            c => c.Name,
                            c => c.DecentDbType,
                            StringComparer.OrdinalIgnoreCase);

                        var colTypes = copyColNames.Select(c =>
                            colTypeMap.TryGetValue(c, out var t) ? t : "TEXT").ToList();

                        var colsSql = string.Join(", ", dstCols.Select(QuoteIdentifier));
                        var placeholders = BuildPlaceholders(colTypes);
                        var insertSql = $"INSERT INTO {QuoteIdentifier(dstTable)} ({colsSql}) VALUES ({string.Join(", ", placeholders)})";

                        long tableRowCount = 0;
                        long tableTotal = schema.RowCounts.GetValueOrDefault(tableName, 0);
                        var batchSize = options.CommitBatchSize > 0 ? options.CommitBatchSize : 5_000;
                        var inTx = false;

                        ExecuteNonQuery(decentConn, "BEGIN");
                        inTx = true;

                        while ((line = reader.ReadLine()) is not null)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (line == @"\.")
                                break;

                            var values = ParseCopyRow(line, colTypes);
                            if (values.Count != copyColNames.Count)
                            {
                                report.Warnings.Add(
                                    $"Row column count mismatch in {tableName}: expected {copyColNames.Count}, got {values.Count}");
                                continue;
                            }

                            using var cmd = decentConn.CreateCommand();
                            cmd.CommandText = insertSql;
                            for (var pi = 0; pi < values.Count; pi++)
                            {
                                var param = cmd.CreateParameter();
                                param.ParameterName = $"@p{pi}";
                                param.Value = values[pi];
                                cmd.Parameters.Add(param);
                            }
                            cmd.ExecuteNonQuery();

                            tableRowCount++;
                            globalRowsCompleted++;

                            if (inTx && tableRowCount % batchSize == 0)
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

                        if (inTx)
                        {
                            ExecuteNonQuery(decentConn, "COMMIT");
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
                }, ct);
            }

            // Phase 4: Create indexes
            var singleColIndexes = new List<PgIndex>();
            var skippedIndexes = new List<SkippedIndex>();

            foreach (var idx in schema.Indexes)
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

                // Mark unique single-column indexes on the table columns
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

    // -- COPY block pattern (reused in second pass) --

    private static readonly System.Text.RegularExpressions.Regex CopyBlockPattern = new(
        @"^COPY\s+(?:(?:public|""public"")\.)?""?(\w+)""?\s*\((.+)\)\s+FROM\s+stdin\s*;",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    #region SQL Generation

    internal static string QuoteIdentifier(string name)
    {
        return "\"" + name.Replace("\"", "\"\"") + "\"";
    }

    internal static string BuildCreateTableSql(PgTable table, bool lowercaseIdentifiers)
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

    #region COPY Data Parsing

    /// <summary>
    /// Parse a tab-separated COPY data row into a list of parameter values.
    /// Handles \N (NULL), \\ (backslash), \n, \r, \t escapes, and boolean conversion.
    /// </summary>
    internal static List<object> ParseCopyRow(string line, List<string> colTypes)
    {
        var fields = SplitCopyFields(line);
        var values = new List<object>(fields.Count);

        for (var i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var colType = i < colTypes.Count ? colTypes[i] : "TEXT";

            if (field == @"\N")
            {
                values.Add(DBNull.Value);
                continue;
            }

            var unescaped = UnescapeCopyField(field);

            if (colType == "BOOL")
            {
                values.Add(unescaped is "t" or "true" or "1");
                continue;
            }

            if (colType == "INT64")
            {
                if (long.TryParse(unescaped, out var l))
                {
                    values.Add(l);
                    continue;
                }
            }

            if (colType == "FLOAT64")
            {
                if (double.TryParse(unescaped, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
                {
                    values.Add(d);
                    continue;
                }
            }

            values.Add(unescaped);
        }

        return values;
    }

    /// <summary>
    /// Split a COPY data line by tab characters. Does not unescape field values.
    /// </summary>
    internal static List<string> SplitCopyFields(string line)
    {
        return [.. line.Split('\t')];
    }

    /// <summary>
    /// Unescape a COPY field value: <c>\\</c> → <c>\</c>, <c>\n</c> → newline,
    /// <c>\r</c> → carriage return, <c>\t</c> → tab.
    /// </summary>
    internal static string UnescapeCopyField(string field)
    {
        if (!field.Contains('\\'))
            return field;

        var sb = new StringBuilder(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 1 < field.Length)
            {
                var next = field[i + 1];
                switch (next)
                {
                    case '\\':
                        sb.Append('\\');
                        i++;
                        break;
                    case 'n':
                        sb.Append('\n');
                        i++;
                        break;
                    case 'r':
                        sb.Append('\r');
                        i++;
                        break;
                    case 't':
                        sb.Append('\t');
                        i++;
                        break;
                    default:
                        sb.Append(field[i]);
                        break;
                }
            }
            else
            {
                sb.Append(field[i]);
            }
        }

        return sb.ToString();
    }

    private static void SkipCopyData(TextReader reader)
    {
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line == @"\.")
                break;
        }
    }

    #endregion

    #region Helpers

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
