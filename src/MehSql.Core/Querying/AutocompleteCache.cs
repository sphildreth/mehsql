using MehSql.Core.Schema;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MehSql.Core.Querying;

/// <summary>
/// Pre-builds O(1) lookup indices for fast autocomplete over schema metadata.
/// </summary>
public class AutocompleteCache
{
    private readonly Dictionary<string, TableNode> _tableIndex;
    private readonly Dictionary<string, List<ColumnNode>> _columnIndex;
    private readonly List<AutocompleteItem> _allTables;
    private readonly List<AutocompleteItem> _allKeywords;

    public AutocompleteCache(SchemaRootNode schema)
    {
        _tableIndex = schema.Tables.ToDictionary(
            t => t.Name,
            StringComparer.OrdinalIgnoreCase
        );

        _columnIndex = schema.Tables.ToDictionary(
            t => t.Name,
            t => t.Columns.ToList(),
            StringComparer.OrdinalIgnoreCase
        );

        _allTables = schema.Tables
            .Select(t => new AutocompleteItem
            {
                DisplayText = t.Name,
                InsertText = t.Name,
                Type = AutocompleteItemType.Table,
                Priority = 100
            })
            .ToList();

        _allKeywords = SqlKeywords.All
            .Select(k => new AutocompleteItem
            {
                DisplayText = k,
                InsertText = k,
                Type = AutocompleteItemType.Keyword,
                Priority = 90
            })
            .ToList();
    }

    /// <summary>
    /// Gets columns for a table by name (case-insensitive).
    /// </summary>
    public List<ColumnNode> GetColumnsForTable(string tableName)
    {
        return _columnIndex.TryGetValue(tableName, out var cols)
            ? cols
            : new List<ColumnNode>();
    }

    /// <summary>
    /// Gets a table node by name (case-insensitive).
    /// </summary>
    public TableNode? GetTable(string tableName)
    {
        return _tableIndex.TryGetValue(tableName, out var table) ? table : null;
    }

    public IReadOnlyList<AutocompleteItem> GetAllTables() => _allTables;
    public IReadOnlyList<AutocompleteItem> GetAllKeywords() => _allKeywords;
}

/// <summary>
/// Represents a single autocomplete suggestion.
/// </summary>
public class AutocompleteItem
{
    public string DisplayText { get; set; } = string.Empty;
    public string InsertText { get; set; } = string.Empty;
    public AutocompleteItemType Type { get; set; }
    public string? Description { get; set; }
    public int Priority { get; set; }
}

public enum AutocompleteItemType
{
    Keyword,
    Table,
    Column,
    Function
}

/// <summary>
/// Standard SQL keywords offered during autocomplete.
/// </summary>
public static class SqlKeywords
{
    public static readonly string[] All =
    [
        "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE",
        "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "CROSS",
        "ON", "AND", "OR", "NOT", "IN", "EXISTS", "BETWEEN", "LIKE",
        "ORDER", "BY", "GROUP", "HAVING", "LIMIT", "OFFSET",
        "AS", "DISTINCT", "UNION", "ALL", "CREATE", "DROP", "ALTER"
    ];
}
