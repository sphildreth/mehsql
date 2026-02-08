namespace MehSql.Core.Import;

/// <summary>
/// Represents a column in a SQLite table schema.
/// </summary>
/// <param name="Name">Column name as declared in SQLite.</param>
/// <param name="DeclaredType">SQLite declared type string (e.g. "INTEGER", "TEXT", "REAL").</param>
/// <param name="NotNull">Whether the column has a NOT NULL constraint.</param>
/// <param name="IsPrimaryKey">Whether the column is part of the primary key.</param>
/// <param name="IsUnique">Whether the column has a single-column UNIQUE constraint.</param>
public sealed record SqliteColumn(
    string Name,
    string DeclaredType,
    bool NotNull,
    bool IsPrimaryKey,
    bool IsUnique = false);

/// <summary>
/// Represents a foreign key relationship in a SQLite table.
/// </summary>
public sealed record SqliteForeignKey(
    string FromColumn,
    string ToTable,
    string ToColumn);

/// <summary>
/// Represents a single-column index in a SQLite table.
/// </summary>
public sealed record SqliteIndex(
    string Name,
    string Table,
    string Column,
    bool IsUnique);

/// <summary>
/// Represents an index that was skipped during import with the reason.
/// </summary>
public sealed record SkippedIndex(
    string Name,
    string Table,
    string Reason);

/// <summary>
/// Represents the full schema of a SQLite table including columns, foreign keys, and indexes.
/// </summary>
public sealed record SqliteTable(
    string Name,
    List<SqliteColumn> Columns,
    List<SqliteForeignKey> ForeignKeys,
    List<SqliteIndex> Indexes,
    List<SkippedIndex> SkippedIndexes);
