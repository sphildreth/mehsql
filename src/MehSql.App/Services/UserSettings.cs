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
}
