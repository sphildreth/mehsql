using System.Globalization;
using System.Text;
using System.Text.Json;
using Serilog;

namespace MehSql.Core.Import;

/// <summary>
/// Metadata from the per-table JSON file in a MySQL Shell dump.
/// </summary>
public sealed class MysqlShellTableMeta
{
    public required string Schema { get; init; }
    public required string TableName { get; init; }
    public required List<string> Columns { get; init; }
    public string? PrimaryIndex { get; init; }
    public string Compression { get; init; } = "zstd";
    public string FieldsTerminatedBy { get; init; } = "\t";
    public string FieldsEscapedBy { get; init; } = "\\";
    public string Extension { get; init; } = "tsv.zst";
    public bool Chunking { get; init; }
}

/// <summary>
/// Complete parsed schema for a MySQL Shell dump directory.
/// </summary>
public sealed class MysqlShellDumpSchema
{
    public string? DefaultSchema { get; init; }
    public List<MysqlTable> Tables { get; init; } = [];
    public Dictionary<string, MysqlShellTableMeta> TableMetas { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Warnings { get; init; } = [];
}

/// <summary>
/// Parses MySQL Shell dump directories, reading JSON metadata and SQL schema files.
/// </summary>
public sealed class MysqlShellDumpParser
{
    /// <summary>
    /// Parse the dump directory, reading JSON metadata and SQL schema files.
    /// </summary>
    internal static MysqlShellDumpSchema ParseDumpDirectory(string dumpDir)
    {
        var warnings = new List<string>();
        var tables = new List<MysqlTable>();
        var tableMetas = new Dictionary<string, MysqlShellTableMeta>(StringComparer.OrdinalIgnoreCase);

        // Read @.json for schema names
        var atJsonPath = Path.Combine(dumpDir, "@.json");
        if (!File.Exists(atJsonPath))
        {
            throw new FileNotFoundException("MySQL Shell dump metadata file @.json not found.", atJsonPath);
        }

        string? defaultSchema = null;
        var schemas = new List<string>();

        using (var doc = JsonDocument.Parse(File.ReadAllText(atJsonPath, Encoding.UTF8)))
        {
            var root = doc.RootElement;

            if (root.TryGetProperty("schemas", out var schemasEl) && schemasEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in schemasEl.EnumerateArray())
                {
                    var name = s.GetString();
                    if (name is not null)
                        schemas.Add(name);
                }
            }

            if (schemas.Count > 0)
                defaultSchema = schemas[0];
        }

        if (schemas.Count == 0)
        {
            warnings.Add("No schemas found in @.json");
            return new MysqlShellDumpSchema
            {
                DefaultSchema = null,
                Tables = tables,
                TableMetas = tableMetas,
                Warnings = warnings
            };
        }

        // Find all per-table JSON metadata files: {schema}@{table}.json
        foreach (var schema in schemas)
        {
            var prefix = schema + "@";
            var jsonFiles = Directory.GetFiles(dumpDir, $"{prefix}*.json", SearchOption.TopDirectoryOnly);

            foreach (var jsonFile in jsonFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(jsonFile);

                // Skip schema-level metadata files (just "{schema}" without "@table")
                if (!fileName.Contains('@'))
                    continue;

                var tableName = fileName[(prefix.Length)..];
                if (string.IsNullOrEmpty(tableName))
                    continue;

                // Parse per-table JSON metadata
                MysqlShellTableMeta? meta;
                try
                {
                    meta = ParseTableMetaJson(jsonFile, schema, tableName);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to parse metadata for {schema}.{tableName}: {ex.Message}");
                    continue;
                }

                var metaKey = $"{schema}.{tableName}";
                tableMetas[metaKey] = meta;

                // Parse corresponding SQL file for CREATE TABLE DDL
                var sqlFile = Path.Combine(dumpDir, $"{schema}@{tableName}.sql");
                if (!File.Exists(sqlFile))
                {
                    warnings.Add($"SQL schema file not found for {schema}.{tableName}");
                    continue;
                }

                MysqlTable? parsedTable;
                try
                {
                    using var reader = new StreamReader(sqlFile, Encoding.UTF8);
                    var dumpSchema = MysqlDumpParser.ParseSchema(reader);
                    parsedTable = dumpSchema.Tables.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to parse SQL for {schema}.{tableName}: {ex.Message}");
                    continue;
                }

                if (parsedTable is null)
                {
                    warnings.Add($"No CREATE TABLE found in {Path.GetFileName(sqlFile)}");
                    continue;
                }

                tables.Add(parsedTable);
                Log.Debug("Parsed table {Schema}.{Table}: {ColCount} columns",
                    schema, tableName, parsedTable.Columns.Count);
            }
        }

        return new MysqlShellDumpSchema
        {
            DefaultSchema = defaultSchema,
            Tables = tables,
            TableMetas = tableMetas,
            Warnings = warnings
        };
    }

    /// <summary>
    /// Get sorted list of data chunk files for a table.
    /// </summary>
    internal static List<string> GetDataChunkFiles(string dumpDir, string schema, string tableName)
    {
        var pattern = $"{schema}@{tableName}@@*.tsv.zst";
        var files = Directory.GetFiles(dumpDir, pattern, SearchOption.TopDirectoryOnly);

        // Natural sort by chunk number
        Array.Sort(files, (a, b) =>
        {
            var numA = ExtractChunkNumber(a, schema, tableName);
            var numB = ExtractChunkNumber(b, schema, tableName);
            return numA.CompareTo(numB);
        });

        return [.. files];
    }

    /// <summary>
    /// Parse a single TSV line into values, handling escape sequences.
    /// </summary>
    internal static object?[] ParseTsvLine(string line, IReadOnlyList<MysqlColumn> columns, string escapedBy = "\\")
    {
        var fields = SplitTsvFields(line);
        var values = new object?[columns.Count];

        for (var i = 0; i < columns.Count; i++)
        {
            if (i >= fields.Count)
            {
                values[i] = DBNull.Value;
                continue;
            }

            var field = fields[i];

            // \N means NULL
            if (field == @"\N")
            {
                values[i] = DBNull.Value;
                continue;
            }

            var unescaped = UnescapeTsvField(field, escapedBy);
            values[i] = ConvertToTyped(unescaped, columns[i].DecentDbType);
        }

        return values;
    }

    #region Private helpers

    /// <summary>
    /// Parse the per-table JSON metadata file.
    /// </summary>
    private static MysqlShellTableMeta ParseTableMetaJson(string jsonPath, string schema, string tableName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath, Encoding.UTF8));
        var root = doc.RootElement;

        var columns = new List<string>();
        string? primaryIndex = null;
        var compression = "zstd";
        var fieldsTerminatedBy = "\t";
        var fieldsEscapedBy = "\\";
        var extension = "tsv.zst";
        var chunking = false;

        if (root.TryGetProperty("options", out var optionsEl))
        {
            if (optionsEl.TryGetProperty("columns", out var colsEl) && colsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var col in colsEl.EnumerateArray())
                {
                    var name = col.GetString();
                    if (name is not null)
                        columns.Add(name);
                }
            }

            if (optionsEl.TryGetProperty("primaryIndex", out var pkEl))
                primaryIndex = pkEl.GetString();

            if (optionsEl.TryGetProperty("compression", out var compEl))
                compression = compEl.GetString() ?? "zstd";

            if (optionsEl.TryGetProperty("fieldsTerminatedBy", out var ftbEl))
                fieldsTerminatedBy = ftbEl.GetString() ?? "\t";

            if (optionsEl.TryGetProperty("fieldsEscapedBy", out var febEl))
                fieldsEscapedBy = febEl.GetString() ?? "\\";
        }

        if (root.TryGetProperty("extension", out var extEl))
            extension = extEl.GetString() ?? "tsv.zst";

        if (root.TryGetProperty("chunking", out var chunkEl) && chunkEl.ValueKind == JsonValueKind.True)
            chunking = true;

        return new MysqlShellTableMeta
        {
            Schema = schema,
            TableName = tableName,
            Columns = columns,
            PrimaryIndex = primaryIndex,
            Compression = compression,
            FieldsTerminatedBy = fieldsTerminatedBy,
            FieldsEscapedBy = fieldsEscapedBy,
            Extension = extension,
            Chunking = chunking
        };
    }

    /// <summary>
    /// Extract the chunk number from a data file path like {schema}@{table}@@{num}.tsv.zst.
    /// </summary>
    private static long ExtractChunkNumber(string filePath, string schema, string tableName)
    {
        var fileName = Path.GetFileName(filePath);
        var prefix = $"{schema}@{tableName}@@";

        if (!fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return long.MaxValue;

        var rest = fileName[prefix.Length..];
        var dotIdx = rest.IndexOf('.');
        if (dotIdx > 0)
            rest = rest[..dotIdx];

        return long.TryParse(rest, out var num) ? num : long.MaxValue;
    }

    /// <summary>
    /// Split a TSV line by tab characters without unescaping.
    /// </summary>
    private static List<string> SplitTsvFields(string line)
    {
        return [.. line.Split('\t')];
    }

    /// <summary>
    /// Unescape a TSV field: <c>\\</c> → <c>\</c>, <c>\t</c> → tab,
    /// <c>\n</c> → newline, <c>\r</c> → CR, <c>\0</c> → NUL.
    /// </summary>
    private static string UnescapeTsvField(string field, string escapedBy)
    {
        if (escapedBy.Length == 0 || !field.Contains(escapedBy[0]))
            return field;

        var escapeChar = escapedBy[0];
        var sb = new StringBuilder(field.Length);

        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == escapeChar && i + 1 < field.Length)
            {
                var next = field[i + 1];
                switch (next)
                {
                    case '\\':
                        sb.Append('\\');
                        i++;
                        break;
                    case 't':
                        sb.Append('\t');
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
                    case '0':
                        sb.Append('\0');
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

    /// <summary>
    /// Convert an unescaped string value to a typed CLR value based on the DecentDB column type.
    /// </summary>
    private static object ConvertToTyped(string value, string decentDbType)
    {
        if (decentDbType == "BOOL")
            return value is "1" or "true" or "TRUE";

        if (decentDbType == "INT64")
        {
            if (long.TryParse(value, out var l))
                return l;
        }

        if (decentDbType == "FLOAT64")
        {
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                return d;
        }

        // DECIMAL and other types: keep as string
        return value;
    }

    #endregion
}
