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
    /// Last active database path (restored on next launch).
    /// </summary>
    public string? LastDatabasePath { get; set; }

    /// <summary>
    /// Saved window position and size for restoring on next launch.
    /// </summary>
    public WindowBounds? Window { get; set; }
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
