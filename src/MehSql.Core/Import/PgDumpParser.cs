using System.Text.RegularExpressions;

namespace MehSql.Core.Import;

/// <summary>
/// Represents a column parsed from a PostgreSQL dump file.
/// </summary>
/// <param name="Name">Column name (unquoted).</param>
/// <param name="PgType">Original PostgreSQL type string.</param>
/// <param name="DecentDbType">Mapped DecentDB type.</param>
/// <param name="NotNull">Whether the column has a NOT NULL constraint.</param>
/// <param name="IsPrimaryKey">Whether the column is part of the primary key.</param>
/// <param name="IsUnique">Whether the column has a single-column UNIQUE constraint.</param>
public sealed record PgColumn(
    string Name,
    string PgType,
    string DecentDbType,
    bool NotNull,
    bool IsPrimaryKey = false,
    bool IsUnique = false);

/// <summary>
/// Represents a table parsed from a PostgreSQL dump file.
/// </summary>
public sealed record PgTable(
    string Name,
    List<PgColumn> Columns,
    List<PgForeignKey> ForeignKeys);

/// <summary>
/// Represents a foreign key relationship parsed from a PostgreSQL dump file.
/// </summary>
public sealed record PgForeignKey(
    string ConstraintName,
    string FromColumn,
    string ToTable,
    string ToColumn);

/// <summary>
/// Represents an index parsed from a PostgreSQL dump file.
/// </summary>
public sealed record PgIndex(
    string Name,
    string Table,
    List<string> Columns,
    bool IsUnique);

/// <summary>
/// Result of parsing the schema from a pg_dump file.
/// </summary>
public sealed class PgDumpSchema
{
    public List<PgTable> Tables { get; } = [];
    public List<PgIndex> Indexes { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>
    /// Row counts observed from COPY blocks during the schema parse pass.
    /// </summary>
    internal Dictionary<string, long> RowCounts { get; } = new();
}

/// <summary>
/// Streaming line-oriented parser for PostgreSQL <c>pg_dump --format=plain</c> SQL dump files.
/// Extracts CREATE TABLE, ALTER TABLE (PK/FK), CREATE INDEX, and COPY block metadata.
/// </summary>
public sealed class PgDumpParser
{
    // -- Regex patterns for line matching --

    private static readonly Regex CreateTableRx = new(
        @"^CREATE\s+TABLE\s+(?:(?:public|""public"")\.)?""?(\w+)""?\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PrimaryKeyRx = new(
        @"^ALTER\s+TABLE\s+(?:ONLY\s+)?(?:(?:public|""public"")\.)?""?(\w+)""?\s+ADD\s+CONSTRAINT\s+""?(\w+)""?\s+PRIMARY\s+KEY\s*\((.+)\)\s*;",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ForeignKeyRx = new(
        @"^ALTER\s+TABLE\s+(?:ONLY\s+)?(?:(?:public|""public"")\.)?""?(\w+)""?\s+ADD\s+CONSTRAINT\s+""?(\w+)""?\s+FOREIGN\s+KEY\s*\(""?(\w+)""?\)\s+REFERENCES\s+(?:(?:public|""public"")\.)?""?(\w+)""?\s*\(""?(\w+)""?\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CreateIndexRx = new(
        @"^CREATE\s+(UNIQUE\s+)?INDEX\s+""?(\w+)""?\s+ON\s+(?:(?:public|""public"")\.)?""?(\w+)""?\s+(?:USING\s+\w+\s*)?\((.+)\)\s*;",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CopyRx = new(
        @"^COPY\s+(?:(?:public|""public"")\.)?""?(\w+)""?\s*\((.+)\)\s+FROM\s+stdin\s*;",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parse the schema (DDL) from a pg_dump plain-text file.
    /// Reads the entire file but only retains schema metadata and COPY row counts.
    /// Does not store row data in memory.
    /// </summary>
    internal static PgDumpSchema ParseSchema(TextReader reader)
    {
        var schema = new PgDumpSchema();
        var tablesByName = new Dictionary<string, PgTable>(StringComparer.OrdinalIgnoreCase);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.TrimStart();

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--"))
                continue;

            // CREATE TABLE
            var createMatch = CreateTableRx.Match(trimmed);
            if (createMatch.Success)
            {
                var tableName = NormalizeIdentifier(createMatch.Groups[1].Value);
                var columns = ParseCreateTableColumns(reader);
                var table = new PgTable(tableName, columns, []);
                schema.Tables.Add(table);
                tablesByName[tableName] = table;
                continue;
            }

            // ALTER TABLE ... PRIMARY KEY
            var pkMatch = PrimaryKeyRx.Match(trimmed);
            if (pkMatch.Success)
            {
                var tableName = NormalizeIdentifier(pkMatch.Groups[1].Value);
                var pkColumns = ParseColumnList(pkMatch.Groups[3].Value);
                if (tablesByName.TryGetValue(tableName, out var table))
                {
                    var pkSet = new HashSet<string>(pkColumns, StringComparer.OrdinalIgnoreCase);
                    for (var i = 0; i < table.Columns.Count; i++)
                    {
                        if (pkSet.Contains(table.Columns[i].Name))
                        {
                            table.Columns[i] = table.Columns[i] with { IsPrimaryKey = true, NotNull = true };
                        }
                    }
                }
                continue;
            }

            // ALTER TABLE ... FOREIGN KEY
            var fkMatch = ForeignKeyRx.Match(trimmed);
            if (fkMatch.Success)
            {
                var tableName = NormalizeIdentifier(fkMatch.Groups[1].Value);
                var constraintName = NormalizeIdentifier(fkMatch.Groups[2].Value);
                var fromCol = NormalizeIdentifier(fkMatch.Groups[3].Value);
                var toTable = NormalizeIdentifier(fkMatch.Groups[4].Value);
                var toCol = NormalizeIdentifier(fkMatch.Groups[5].Value);
                if (tablesByName.TryGetValue(tableName, out var table))
                {
                    table.ForeignKeys.Add(new PgForeignKey(constraintName, fromCol, toTable, toCol));
                }
                continue;
            }

            // CREATE INDEX
            var idxMatch = CreateIndexRx.Match(trimmed);
            if (idxMatch.Success)
            {
                var isUnique = !string.IsNullOrWhiteSpace(idxMatch.Groups[1].Value);
                var indexName = NormalizeIdentifier(idxMatch.Groups[2].Value);
                var tableName = NormalizeIdentifier(idxMatch.Groups[3].Value);
                var columns = ParseColumnList(idxMatch.Groups[4].Value);
                schema.Indexes.Add(new PgIndex(indexName, tableName, columns, isUnique));
                continue;
            }

            // COPY ... FROM stdin — count data rows without storing them
            var copyMatch = CopyRx.Match(trimmed);
            if (copyMatch.Success)
            {
                var tableName = NormalizeIdentifier(copyMatch.Groups[1].Value);
                long rowCount = 0;
                while ((line = reader.ReadLine()) is not null)
                {
                    if (line == @"\.")
                        break;
                    rowCount++;
                }
                schema.RowCounts[tableName] = rowCount;
            }
        }

        return schema;
    }

    /// <summary>
    /// Parse column definitions from inside a CREATE TABLE block.
    /// Reads lines until the closing <c>);</c> is found.
    /// </summary>
    private static List<PgColumn> ParseCreateTableColumns(TextReader reader)
    {
        var columns = new List<PgColumn>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();

            // End of CREATE TABLE
            if (trimmed.StartsWith(");"))
                break;

            // Skip table-level constraints (CONSTRAINT, PRIMARY KEY, UNIQUE, CHECK, FOREIGN KEY)
            if (trimmed.StartsWith("CONSTRAINT ", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("UNIQUE", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("CHECK", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
                continue;

            // Expect: "ColName" type [NOT NULL] [DEFAULT ...][,]
            var col = ParseColumnLine(trimmed);
            if (col is not null)
                columns.Add(col);
        }

        return columns;
    }

    /// <summary>
    /// Parse a single column definition line from a CREATE TABLE block.
    /// </summary>
    internal static PgColumn? ParseColumnLine(string line)
    {
        // Remove trailing comma
        if (line.EndsWith(','))
            line = line[..^1].TrimEnd();

        if (string.IsNullOrWhiteSpace(line))
            return null;

        // Extract column name (quoted or unquoted)
        string name;
        int pos;
        if (line.StartsWith('"'))
        {
            var endQuote = line.IndexOf('"', 1);
            if (endQuote < 0)
                return null;
            name = line[1..endQuote];
            pos = endQuote + 1;
        }
        else
        {
            var spaceIdx = line.IndexOf(' ');
            if (spaceIdx < 0)
                return null;
            name = line[..spaceIdx];
            pos = spaceIdx;
        }

        var rest = line[pos..].Trim();
        if (string.IsNullOrEmpty(rest))
            return null;

        // Extract type — everything before NOT NULL / DEFAULT / end of string
        var notNull = false;
        var pgType = ExtractPgType(rest, out notNull);

        var decentDbType = MapPgTypeToDecentDb(pgType);
        return new PgColumn(name, pgType, decentDbType, notNull);
    }

    /// <summary>
    /// Extract the PostgreSQL type from the remainder of a column definition line,
    /// also detecting NOT NULL.
    /// </summary>
    private static string ExtractPgType(string rest, out bool notNull)
    {
        notNull = false;

        // Check for NOT NULL / DEFAULT keywords to find where type ends
        var upperRest = rest.ToUpperInvariant();

        // Handle types that contain parenthesized parameters like numeric(10,2) or character varying(255)
        var typeParts = new List<string>();
        var i = 0;
        var parenDepth = 0;

        while (i < rest.Length)
        {
            var ch = rest[i];
            if (ch == '(')
                parenDepth++;
            else if (ch == ')')
                parenDepth--;

            if (parenDepth == 0 && ch == ' ')
            {
                // Check if the next word is a type continuation or a constraint
                var remaining = rest[i..].TrimStart();
                var remainUpper = remaining.ToUpperInvariant();

                // Type continuations: "varying", "without", "with", "precision", "zone", "time"
                if (remainUpper.StartsWith("VARYING") ||
                    remainUpper.StartsWith("WITHOUT TIME ZONE") ||
                    remainUpper.StartsWith("WITH TIME ZONE") ||
                    remainUpper.StartsWith("PRECISION") ||
                    remainUpper.StartsWith("ZONE"))
                {
                    typeParts.Add(rest[..i]);
                    // Continue scanning from here
                    rest = remaining;
                    upperRest = remainUpper;
                    i = 0;
                    continue;
                }

                // Otherwise this space marks the end of the type
                break;
            }

            i++;
        }

        string pgType;
        string afterType;
        if (i < rest.Length)
        {
            var typePart = rest[..i].Trim();
            if (typeParts.Count > 0)
            {
                typeParts.Add(typePart);
                pgType = string.Join(" ", typeParts);
            }
            else
            {
                pgType = typePart;
            }
            afterType = rest[i..];
        }
        else
        {
            if (typeParts.Count > 0)
            {
                typeParts.Add(rest.Trim());
                pgType = string.Join(" ", typeParts);
            }
            else
            {
                pgType = rest.Trim();
            }
            afterType = "";
        }

        // Check for NOT NULL in the remainder
        if (afterType.Contains("NOT NULL", StringComparison.OrdinalIgnoreCase))
            notNull = true;

        return pgType;
    }

    /// <summary>
    /// Parse a comma-separated list of column names, stripping quotes.
    /// </summary>
    internal static List<string> ParseColumnList(string raw)
    {
        return raw.Split(',')
            .Select(c => NormalizeIdentifier(c.Trim()))
            .Where(c => c.Length > 0)
            .ToList();
    }

    /// <summary>
    /// Map a PostgreSQL type string to the closest DecentDB type.
    /// </summary>
    internal static string MapPgTypeToDecentDb(string pgType)
    {
        var t = pgType.Trim().ToUpperInvariant();

        // Array types → TEXT
        if (t.EndsWith("[]"))
            return "TEXT";

        // Remove parenthesized parameters for matching, but keep for DECIMAL
        var baseName = t;
        var parenParams = "";
        var parenIdx = t.IndexOf('(');
        if (parenIdx >= 0)
        {
            baseName = t[..parenIdx].Trim();
            parenParams = t[parenIdx..];
        }

        return baseName switch
        {
            "INTEGER" or "SERIAL" or "INT4" or "INT" => "INT64",
            "BIGINT" or "BIGSERIAL" or "INT8" => "INT64",
            "SMALLINT" or "INT2" or "SMALLSERIAL" => "INT64",
            "BOOLEAN" or "BOOL" => "BOOL",
            "REAL" or "FLOAT4" => "FLOAT64",
            "DOUBLE PRECISION" or "FLOAT8" => "FLOAT64",
            "NUMERIC" or "DECIMAL" =>
                parenParams.Length > 0 ? $"DECIMAL{parenParams}" : "TEXT",
            "CHARACTER VARYING" or "VARCHAR" => "TEXT",
            "CHARACTER" or "CHAR" or "BPCHAR" => "TEXT",
            "TEXT" => "TEXT",
            "UUID" => "UUID",
            "BYTEA" => "BLOB",
            "DATE" or "TIMESTAMP" or "TIMESTAMPTZ" => "TEXT",
            "TIMESTAMP WITH TIME ZONE" => "TEXT",
            "TIMESTAMP WITHOUT TIME ZONE" => "TEXT",
            "JSON" or "JSONB" => "TEXT",
            _ => "TEXT"
        };
    }

    /// <summary>
    /// Strip <c>public.</c> prefix and double-quote wrapping from an identifier.
    /// </summary>
    internal static string NormalizeIdentifier(string raw)
    {
        var s = raw.Trim();

        // Strip schema prefix (public. or "public".)
        if (s.StartsWith("public.", StringComparison.OrdinalIgnoreCase))
            s = s[7..];
        else if (s.StartsWith("\"public\".", StringComparison.OrdinalIgnoreCase))
            s = s[9..];

        // Strip surrounding double quotes
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"')
            s = s[1..^1];

        return s;
    }
}
