using System.Text;
using System.Text.RegularExpressions;

namespace MehSql.Core.Import;

/// <summary>
/// Represents a column parsed from a MySQL dump file.
/// </summary>
/// <param name="Name">Column name (unquoted).</param>
/// <param name="MysqlType">Original MySQL type string.</param>
/// <param name="DecentDbType">Mapped DecentDB type.</param>
/// <param name="NotNull">Whether the column has a NOT NULL constraint.</param>
/// <param name="IsPrimaryKey">Whether the column is part of the primary key.</param>
/// <param name="IsUnique">Whether the column has a single-column UNIQUE constraint.</param>
/// <param name="IsAutoIncrement">Whether the column has AUTO_INCREMENT.</param>
public sealed record MysqlColumn(
    string Name,
    string MysqlType,
    string DecentDbType,
    bool NotNull,
    bool IsPrimaryKey = false,
    bool IsUnique = false,
    bool IsAutoIncrement = false);

/// <summary>
/// Represents a table parsed from a MySQL dump file.
/// </summary>
public sealed record MysqlTable(
    string Name,
    List<MysqlColumn> Columns,
    List<MysqlForeignKey> ForeignKeys,
    List<MysqlIndex> Indexes);

/// <summary>
/// Represents a foreign key relationship parsed from a MySQL dump file.
/// </summary>
public sealed record MysqlForeignKey(
    string ConstraintName,
    string FromColumn,
    string ToTable,
    string ToColumn);

/// <summary>
/// Represents an index parsed from a MySQL dump file.
/// </summary>
public sealed record MysqlIndex(
    string Name,
    string Table,
    List<string> Columns,
    bool IsUnique);

/// <summary>
/// Result of parsing the schema from a mysqldump file.
/// </summary>
public sealed class MysqlDumpSchema
{
    public List<MysqlTable> Tables { get; init; } = [];
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Row counts observed from INSERT statements during the schema parse pass.
    /// </summary>
    internal Dictionary<string, long> RowCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Streaming line-oriented parser for MySQL <c>mysqldump</c> SQL dump files.
/// Extracts CREATE TABLE blocks (columns, PK, FK, indexes) and INSERT INTO row counts.
/// </summary>
public sealed class MysqlDumpParser
{
    private static readonly Regex CreateTableRx = new(
        @"^CREATE\s+TABLE\s+`?(\w+)`?\s*\(",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex InsertIntoRx = new(
        @"^INSERT\s+INTO\s+`?(\w+)`?\s+VALUES\s*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Column definition: `col_name` type[(size)] [UNSIGNED] [NOT NULL] [DEFAULT ...] [AUTO_INCREMENT]
    private static readonly Regex ColumnRx = new(
        @"^`(\w+)`\s+(.+)$",
        RegexOptions.Compiled);

    // Inline PRIMARY KEY (`col1`, `col2`)
    private static readonly Regex InlinePkRx = new(
        @"^PRIMARY\s+KEY\s*\(([^)]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Inline UNIQUE KEY `name` (`col1`)
    private static readonly Regex InlineUniqueRx = new(
        @"^UNIQUE\s+KEY\s+`(\w+)`\s*\(([^)]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Inline KEY `name` (`col1`, `col2`)
    private static readonly Regex InlineKeyRx = new(
        @"^KEY\s+`(\w+)`\s*\(([^)]+)\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Inline CONSTRAINT `name` FOREIGN KEY (`col`) REFERENCES `table` (`col`)
    private static readonly Regex InlineFkRx = new(
        @"^CONSTRAINT\s+`(\w+)`\s+FOREIGN\s+KEY\s*\(`(\w+)`\)\s+REFERENCES\s+`(\w+)`\s*\(`(\w+)`\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parse the schema (DDL) and count INSERT rows from a mysqldump plain-text file.
    /// Reads the entire file but only retains schema metadata and row counts.
    /// </summary>
    internal static MysqlDumpSchema ParseSchema(TextReader reader)
    {
        var schema = new MysqlDumpSchema();
        var tablesByName = new Dictionary<string, MysqlTable>(StringComparer.OrdinalIgnoreCase);

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = StripConditionalComment(line.TrimStart());

            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("--"))
                continue;

            // Skip known irrelevant statements
            if (IsSkippableLine(trimmed))
                continue;

            // CREATE TABLE
            var createMatch = CreateTableRx.Match(trimmed);
            if (createMatch.Success)
            {
                var tableName = createMatch.Groups[1].Value;
                var (columns, fks, indexes, pkColumns, uniqueColumns) = ParseCreateTableBody(reader);

                // Apply PK flags
                var pkSet = new HashSet<string>(pkColumns, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < columns.Count; i++)
                {
                    if (pkSet.Contains(columns[i].Name))
                    {
                        columns[i] = columns[i] with { IsPrimaryKey = true, NotNull = true };
                    }
                }

                // Apply unique flags (single-column unique keys only)
                foreach (var uniqueCol in uniqueColumns)
                {
                    for (var i = 0; i < columns.Count; i++)
                    {
                        if (string.Equals(columns[i].Name, uniqueCol, StringComparison.OrdinalIgnoreCase))
                        {
                            columns[i] = columns[i] with { IsUnique = true };
                        }
                    }
                }

                var table = new MysqlTable(tableName, columns, fks, indexes);
                schema.Tables.Add(table);
                tablesByName[tableName] = table;
                continue;
            }

            // INSERT INTO — count value groups without storing data
            var insertMatch = InsertIntoRx.Match(trimmed);
            if (insertMatch.Success)
            {
                var tableName = insertMatch.Groups[1].Value;
                var valuesPart = trimmed[insertMatch.Length..];
                var rowCount = CountValueGroups(valuesPart, reader);
                schema.RowCounts.TryGetValue(tableName, out var existing);
                schema.RowCounts[tableName] = existing + rowCount;
            }
        }

        return schema;
    }

    /// <summary>
    /// Map a MySQL type string to the closest DecentDB type.
    /// </summary>
    internal static string MapMysqlTypeToDecentDb(string mysqlType)
    {
        var t = mysqlType.Trim().ToUpperInvariant();

        // Remove UNSIGNED/SIGNED/ZEROFILL modifiers for matching
        t = Regex.Replace(t, @"\s+(UNSIGNED|SIGNED|ZEROFILL)", "", RegexOptions.IgnoreCase).Trim();

        // Extract base name and parenthesized parameters
        var baseName = t;
        var parenParams = "";
        var parenIdx = t.IndexOf('(');
        if (parenIdx >= 0)
        {
            baseName = t[..parenIdx].Trim();
            var closeIdx = t.IndexOf(')', parenIdx);
            if (closeIdx >= 0)
                parenParams = t[parenIdx..(closeIdx + 1)];
        }

        // Special case: tinyint(1) → BOOL
        if (baseName == "TINYINT" && parenParams == "(1)")
            return "BOOL";

        return baseName switch
        {
            "INT" or "INTEGER" => "INT64",
            "BIGINT" => "INT64",
            "SMALLINT" => "INT64",
            "MEDIUMINT" => "INT64",
            "TINYINT" => "INT64",
            "FLOAT" => "FLOAT64",
            "DOUBLE" or "DOUBLE PRECISION" => "FLOAT64",
            "REAL" => "FLOAT64",
            "DECIMAL" or "NUMERIC" =>
                parenParams.Length > 0 ? $"DECIMAL{parenParams}" : "DECIMAL(18,6)",
            "VARCHAR" or "CHAR" => "TEXT",
            "TEXT" or "MEDIUMTEXT" or "LONGTEXT" or "TINYTEXT" => "TEXT",
            "DATE" or "DATETIME" or "TIMESTAMP" or "TIME" or "YEAR" => "TEXT",
            "BLOB" or "MEDIUMBLOB" or "LONGBLOB" or "TINYBLOB" => "BLOB",
            "BINARY" or "VARBINARY" => "BLOB",
            "ENUM" or "SET" => "TEXT",
            "JSON" => "TEXT",
            "BIT" => "INT64",
            _ => "TEXT"
        };
    }

    /// <summary>
    /// Strip backtick wrapping from a MySQL identifier.
    /// </summary>
    internal static string StripBackticks(string raw)
    {
        var s = raw.Trim();
        if (s.Length >= 2 && s[0] == '`' && s[^1] == '`')
            s = s[1..^1];
        return s;
    }

    /// <summary>
    /// Parse MySQL INSERT value groups into typed row arrays.
    /// </summary>
    /// <remarks>
    /// Format: <c>(1,'Aruba',193.00,NULL,'AW'),(2,'Afghanistan',...)</c>.
    /// Each element is: <c>string</c>, <c>long</c>, <c>double</c>, <c>DBNull.Value</c>, <c>bool</c>, or <c>byte[]</c>.
    /// </remarks>
    internal static IEnumerable<object?[]> ParseInsertValues(string valuesPart, IReadOnlyList<MysqlColumn> columns)
    {
        var pos = 0;
        while (pos < valuesPart.Length)
        {
            // Find start of value group '('
            while (pos < valuesPart.Length && valuesPart[pos] != '(')
                pos++;

            if (pos >= valuesPart.Length)
                yield break;

            pos++; // skip '('

            var values = ParseSingleValueGroup(valuesPart, ref pos, columns);
            if (values is not null)
                yield return values;
        }
    }

    /// <summary>
    /// Parse a single parenthesized value group starting after the opening '('.
    /// Advances <paramref name="pos"/> past the closing ')'.
    /// </summary>
    private static object?[]? ParseSingleValueGroup(string text, ref int pos, IReadOnlyList<MysqlColumn> columns)
    {
        var values = new List<object?>();
        var colIdx = 0;

        while (pos < text.Length)
        {
            SkipWhitespace(text, ref pos);

            if (pos >= text.Length)
                break;

            var ch = text[pos];

            if (ch == ')')
            {
                pos++; // skip ')'
                break;
            }

            if (ch == ',')
            {
                pos++; // skip comma between values
                continue;
            }

            var colType = colIdx < columns.Count ? columns[colIdx].DecentDbType : "TEXT";

            if (ch == '\'')
            {
                // String value
                var str = ParseMysqlString(text, ref pos);
                values.Add(str);
            }
            else if (ch == 'N' && pos + 3 < text.Length &&
                     text[pos + 1] == 'U' && text[pos + 2] == 'L' && text[pos + 3] == 'L')
            {
                values.Add(DBNull.Value);
                pos += 4;
            }
            else
            {
                // Numeric or other literal value
                var start = pos;
                while (pos < text.Length && text[pos] != ',' && text[pos] != ')')
                    pos++;

                var raw = text[start..pos].Trim();
                values.Add(ConvertValue(raw, colType));
            }

            colIdx++;
        }

        return values.Count > 0 ? [.. values] : null;
    }

    /// <summary>
    /// Parse a MySQL single-quoted string, handling escape sequences.
    /// Assumes <paramref name="pos"/> points at the opening quote.
    /// </summary>
    private static string ParseMysqlString(string text, ref int pos)
    {
        pos++; // skip opening quote
        var sb = new StringBuilder();

        while (pos < text.Length)
        {
            var ch = text[pos];

            if (ch == '\\' && pos + 1 < text.Length)
            {
                var next = text[pos + 1];
                switch (next)
                {
                    case '\'':
                        sb.Append('\'');
                        pos += 2;
                        break;
                    case '\\':
                        sb.Append('\\');
                        pos += 2;
                        break;
                    case 'n':
                        sb.Append('\n');
                        pos += 2;
                        break;
                    case 'r':
                        sb.Append('\r');
                        pos += 2;
                        break;
                    case '0':
                        sb.Append('\0');
                        pos += 2;
                        break;
                    case 't':
                        sb.Append('\t');
                        pos += 2;
                        break;
                    default:
                        sb.Append(next);
                        pos += 2;
                        break;
                }
            }
            else if (ch == '\'')
            {
                pos++; // skip closing quote
                // Handle MySQL '' escape (doubled single quote)
                if (pos < text.Length && text[pos] == '\'')
                {
                    sb.Append('\'');
                    pos++;
                }
                else
                {
                    break;
                }
            }
            else
            {
                sb.Append(ch);
                pos++;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Convert a raw numeric literal to the appropriate CLR type based on the DecentDB column type.
    /// </summary>
    private static object ConvertValue(string raw, string decentDbType)
    {
        if (string.IsNullOrEmpty(raw))
            return DBNull.Value;

        if (decentDbType == "BOOL")
        {
            return raw is "1" or "true" or "TRUE";
        }

        if (decentDbType == "INT64")
        {
            if (long.TryParse(raw, out var l))
                return l;
        }

        if (decentDbType == "FLOAT64")
        {
            if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var d))
                return d;
        }

        if (decentDbType.StartsWith("DECIMAL"))
        {
            // Keep as string for CAST in parameterized insert
            return raw;
        }

        return raw;
    }

    private static void SkipWhitespace(string text, ref int pos)
    {
        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
            pos++;
    }

    #region CREATE TABLE body parsing

    /// <summary>
    /// Parse the body of a CREATE TABLE block (everything after the opening parenthesis).
    /// Returns columns, foreign keys, indexes, primary key column names, and single-column unique key column names.
    /// </summary>
    private static (List<MysqlColumn> Columns, List<MysqlForeignKey> ForeignKeys, List<MysqlIndex> Indexes,
        List<string> PkColumns, List<string> UniqueColumns)
        ParseCreateTableBody(TextReader reader)
    {
        var columns = new List<MysqlColumn>();
        var fks = new List<MysqlForeignKey>();
        var indexes = new List<MysqlIndex>();
        var pkColumns = new List<string>();
        var uniqueColumns = new List<string>();

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();

            // End of CREATE TABLE — the closing line starts with ')' and contains ENGINE= or just );
            if (trimmed.StartsWith(')'))
                break;

            // Remove trailing comma
            if (trimmed.EndsWith(','))
                trimmed = trimmed[..^1].TrimEnd();

            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            // PRIMARY KEY
            var pkMatch = InlinePkRx.Match(trimmed);
            if (pkMatch.Success)
            {
                pkColumns.AddRange(ParseBacktickedList(pkMatch.Groups[1].Value));
                continue;
            }

            // UNIQUE KEY
            var uniqueMatch = InlineUniqueRx.Match(trimmed);
            if (uniqueMatch.Success)
            {
                var idxName = uniqueMatch.Groups[1].Value;
                var idxCols = ParseBacktickedList(uniqueMatch.Groups[2].Value);
                indexes.Add(new MysqlIndex(idxName, "", idxCols, IsUnique: true));
                // Mark single-column unique keys on the column
                if (idxCols.Count == 1)
                    uniqueColumns.Add(idxCols[0]);
                continue;
            }

            // KEY (non-unique index)
            var keyMatch = InlineKeyRx.Match(trimmed);
            if (keyMatch.Success)
            {
                var idxName = keyMatch.Groups[1].Value;
                var idxCols = ParseBacktickedList(keyMatch.Groups[2].Value);
                indexes.Add(new MysqlIndex(idxName, "", idxCols, IsUnique: false));
                continue;
            }

            // CONSTRAINT ... FOREIGN KEY
            var fkMatch = InlineFkRx.Match(trimmed);
            if (fkMatch.Success)
            {
                fks.Add(new MysqlForeignKey(
                    fkMatch.Groups[1].Value,
                    fkMatch.Groups[2].Value,
                    fkMatch.Groups[3].Value,
                    fkMatch.Groups[4].Value));
                continue;
            }

            // Column definition
            var colMatch = ColumnRx.Match(trimmed);
            if (colMatch.Success)
            {
                var colName = colMatch.Groups[1].Value;
                var rest = colMatch.Groups[2].Value;
                var col = ParseColumnDefinition(colName, rest);
                if (col is not null)
                    columns.Add(col);
            }
        }

        return (columns, fks, indexes, pkColumns, uniqueColumns);
    }

    /// <summary>
    /// Parse a single MySQL column definition from the type and modifiers portion.
    /// </summary>
    private static MysqlColumn? ParseColumnDefinition(string name, string rest)
    {
        // Extract the MySQL type — everything before NOT NULL / DEFAULT / AUTO_INCREMENT / COMMENT
        var (mysqlType, notNull, isAutoIncrement) = ExtractMysqlType(rest);
        if (string.IsNullOrEmpty(mysqlType))
            return null;

        var decentDbType = MapMysqlTypeToDecentDb(mysqlType);
        return new MysqlColumn(name, mysqlType, decentDbType, notNull, IsAutoIncrement: isAutoIncrement);
    }

    /// <summary>
    /// Extract the MySQL type from a column definition, also detecting NOT NULL and AUTO_INCREMENT.
    /// Handles parenthesized type parameters like <c>decimal(10,2)</c> and <c>enum('a','b')</c>.
    /// </summary>
    private static (string MysqlType, bool NotNull, bool IsAutoIncrement) ExtractMysqlType(string rest)
    {
        var notNull = false;
        var isAutoIncrement = false;

        var upper = rest.ToUpperInvariant();

        if (upper.Contains("NOT NULL"))
            notNull = true;
        if (upper.Contains("AUTO_INCREMENT"))
            isAutoIncrement = true;

        // Parse the type portion — scan forward handling parentheses for type params
        var i = 0;
        var parenDepth = 0;

        while (i < rest.Length)
        {
            var ch = rest[i];
            if (ch == '(')
                parenDepth++;
            else if (ch == ')')
            {
                parenDepth--;
                if (parenDepth == 0)
                {
                    i++;
                    break;
                }
            }

            if (parenDepth == 0 && ch == ' ')
            {
                var remaining = rest[i..].TrimStart().ToUpperInvariant();
                // Check if next word is a type continuation (UNSIGNED, SIGNED, ZEROFILL, PRECISION)
                if (remaining.StartsWith("UNSIGNED") ||
                    remaining.StartsWith("SIGNED") ||
                    remaining.StartsWith("ZEROFILL") ||
                    remaining.StartsWith("PRECISION"))
                {
                    // Include in type
                    i++;
                    continue;
                }

                // Otherwise this space marks the end of the type
                break;
            }

            i++;
        }

        var mysqlType = rest[..i].Trim();
        return (mysqlType, notNull, isAutoIncrement);
    }

    /// <summary>
    /// Parse a comma-separated list of backtick-quoted identifiers.
    /// </summary>
    private static List<string> ParseBacktickedList(string raw)
    {
        return raw.Split(',')
            .Select(s => StripBackticks(s.Trim()))
            .Where(s => s.Length > 0)
            .ToList();
    }

    #endregion

    #region INSERT row counting

    /// <summary>
    /// Count the number of value groups in INSERT VALUES, potentially spanning multiple lines.
    /// Does not store the actual data.
    /// </summary>
    private static long CountValueGroups(string initialPart, TextReader reader)
    {
        long count = 0;
        var current = initialPart;
        var done = false;

        while (!done)
        {
            count += CountParenGroups(current, out done);
            if (!done)
            {
                var nextLine = reader.ReadLine();
                if (nextLine is null)
                    break;
                current = nextLine;
            }
        }

        return count;
    }

    /// <summary>
    /// Count top-level <c>(...)</c> groups in a string fragment, respecting strings.
    /// Sets <paramref name="complete"/> to true when a semicolon is found at the end.
    /// </summary>
    private static long CountParenGroups(string text, out bool complete)
    {
        long count = 0;
        var inString = false;
        var depth = 0;
        complete = false;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inString)
            {
                if (ch == '\\' && i + 1 < text.Length)
                {
                    i++; // skip escaped char
                }
                else if (ch == '\'')
                {
                    inString = false;
                }
                continue;
            }

            switch (ch)
            {
                case '\'':
                    inString = true;
                    break;
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0)
                        count++;
                    break;
                case ';':
                    complete = true;
                    return count;
            }
        }

        return count;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Strip MySQL conditional comment wrappers <c>/*!nnnnn ... */</c> and return the inner content.
    /// If the line is purely a conditional comment with no useful SQL, returns empty string.
    /// </summary>
    private static string StripConditionalComment(string line)
    {
        if (!line.StartsWith("/*!"))
            return line;

        // Entire line is a conditional comment — skip it
        return "";
    }

    /// <summary>
    /// Returns true for lines that should be skipped entirely.
    /// </summary>
    private static bool IsSkippableLine(string trimmed)
    {
        if (trimmed.StartsWith("LOCK TABLES", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.StartsWith("UNLOCK TABLES", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.StartsWith("DROP TABLE", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.StartsWith("SET ", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.StartsWith("USE ", StringComparison.OrdinalIgnoreCase))
            return true;
        if (trimmed.StartsWith("CREATE DATABASE", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    #endregion
}
