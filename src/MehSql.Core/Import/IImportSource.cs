namespace MehSql.Core.Import;

/// <summary>
/// Detected import format.
/// </summary>
public enum ImportFormat
{
    SQLite,
    PgDump,
    MysqlDump,
    MysqlShellDump,
    Unknown
}

/// <summary>
/// Format-agnostic analysis result for any import source.
/// </summary>
public sealed class GenericAnalysisResult
{
    public required string SourcePath { get; init; }
    public required ImportFormat Format { get; init; }
    public List<string> TableNames { get; init; } = [];
    public Dictionary<string, long> RowCounts { get; init; } = new();
    public long TotalRows => RowCounts.Values.Sum();
    public List<string> Warnings { get; init; } = [];

    /// <summary>
    /// Format-specific analysis for SQLite (null for other formats).
    /// </summary>
    public AnalysisResult? SqliteAnalysis { get; init; }
}

/// <summary>
/// Format-agnostic import options.
/// </summary>
public sealed class GenericImportOptions
{
    public required string SourcePath { get; init; }
    public required string DecentDbPath { get; init; }
    public required ImportFormat Format { get; init; }
    public bool LowercaseIdentifiers { get; init; } = true;
    public int CommitBatchSize { get; init; } = 5_000;
    public bool Overwrite { get; init; }

    /// <summary>
    /// Working directory for extracted archives. Cleaned up after import.
    /// </summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>
/// Common interface for all database import sources (SQLite, pg_dump, mysqldump, etc.).
/// </summary>
public interface IImportSource
{
    /// <summary>
    /// The format this source handles.
    /// </summary>
    ImportFormat Format { get; }

    /// <summary>
    /// Analyze the source and return a summary (tables, estimated row counts, warnings).
    /// </summary>
    Task<GenericAnalysisResult> AnalyzeAsync(string sourcePath, CancellationToken ct = default);

    /// <summary>
    /// Run the full import: create schema, copy data, create indexes in the DecentDB file.
    /// </summary>
    Task<ImportReport> ImportAsync(
        GenericImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default);
}
