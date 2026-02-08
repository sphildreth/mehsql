using System.Diagnostics;

namespace MehSql.Core.Import;

/// <summary>
/// Phase of the import process.
/// </summary>
public enum ImportPhase
{
    Analyzing,
    CreatingSchema,
    CopyingData,
    CreatingIndexes,
    Complete,
    Failed,
    Cancelled
}

/// <summary>
/// Progress update reported during import. Designed for IProgress&lt;ImportProgress&gt;.
/// </summary>
public sealed class ImportProgress
{
    public ImportPhase Phase { get; init; }
    public string? CurrentTable { get; init; }
    public long RowsCompleted { get; init; }
    public long RowsTotal { get; init; }
    public int TablesCompleted { get; init; }
    public int TablesTotal { get; init; }
    public int IndexesCompleted { get; init; }
    public int IndexesTotal { get; init; }
    public string Message { get; init; } = "";
}

/// <summary>
/// Options for controlling the SQLite import process.
/// </summary>
public sealed class ImportOptions
{
    public required string SqlitePath { get; init; }
    public required string DecentDbPath { get; init; }
    public bool LowercaseIdentifiers { get; init; } = true;
    public int CommitBatchSize { get; init; } = 5_000;
    public bool Overwrite { get; init; }
}

/// <summary>
/// Summary of a completed import operation.
/// </summary>
public sealed class ImportReport
{
    public required string SqlitePath { get; init; }
    public required string DecentDbPath { get; init; }
    public List<string> Tables { get; init; } = [];
    public Dictionary<string, long> RowsCopied { get; init; } = new();
    public List<string> IndexesCreated { get; init; } = [];
    public List<string> UniqueColumnsAdded { get; init; } = [];
    public List<SkippedIndex> SkippedIndexes { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public TimeSpan Elapsed { get; set; }
    public long TotalRows => RowsCopied.Values.Sum();
}

/// <summary>
/// Result from analyzing a SQLite database before import.
/// </summary>
public sealed class AnalysisResult
{
    public required string SqlitePath { get; init; }
    public List<SqliteTable> Tables { get; init; } = [];
    public Dictionary<string, long> RowCounts { get; init; } = new();
    public long TotalRows => RowCounts.Values.Sum();
    public List<SkippedIndex> SkippedIndexes { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
}
