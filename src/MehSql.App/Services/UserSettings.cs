using System.Collections.Generic;

namespace MehSql.App.Services;

/// <summary>
/// Persistent user settings serialized to YAML.
/// </summary>
public sealed class UserSettings
{
    /// <summary>
    /// Most recently opened database file paths, newest first. Capped at 10.
    /// </summary>
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>
    /// Most recently opened SQL file paths, newest first. Capped at 10.
    /// </summary>
    public List<string> RecentSqlFiles { get; set; } = [];

    /// <summary>
    /// Last active database path (restored on next launch).
    /// </summary>
    public string? LastDatabasePath { get; set; }

    /// <summary>
    /// Selected UI theme ("Dark" or "Light").
    /// </summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>
    /// Custom temp folder for decompression and import staging.
    /// Null or empty = use system default (Path.GetTempPath()).
    /// </summary>
    public string? TempFolder { get; set; }

    /// <summary>
    /// Saved window position and size for restoring on next launch.
    /// </summary>
    public WindowBounds? Window { get; set; }

    /// <summary>
    /// Maximum query history entries retained per database.
    /// </summary>
    public int QueryHistoryLimit { get; set; } = 200;

    /// <summary>
    /// Query history entries across databases.
    /// </summary>
    public List<QueryHistoryRecord> QueryHistory { get; set; } = [];
}

/// <summary>
/// Captures window position, size, and state for persistence across sessions.
/// </summary>
public sealed class WindowBounds
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>
    /// Stored as string to avoid enum serialization issues (Normal, Maximized).
    /// </summary>
    public string State { get; set; } = "Normal";
}

public sealed class QueryHistoryRecord
{
    public string DatabasePath { get; set; } = string.Empty;
    public string Sql { get; set; } = string.Empty;
    public System.DateTimeOffset ExecutedAtUtc { get; set; } = System.DateTimeOffset.UtcNow;
}
