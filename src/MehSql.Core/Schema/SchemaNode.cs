using System;
using System.Collections.Generic;

namespace MehSql.Core.Schema;

/// <summary>
/// Base class for all schema nodes in the tree.
/// </summary>
public abstract class SchemaNode
{
    public string Name { get; }
    public string DisplayName => Name;
    public abstract string NodeType { get; }

    protected SchemaNode(string name)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }
}

/// <summary>
/// Root node representing a database/schema.
/// </summary>
public sealed class SchemaRootNode : SchemaNode
{
    public override string NodeType => "Schema";
    public List<TableNode> Tables { get; } = new();
    public List<ViewNode> Views { get; } = new();

    public SchemaRootNode(string name) : base(name) { }
}

/// <summary>
/// Represents a database table.
/// </summary>
public sealed class TableNode : SchemaNode
{
    public override string NodeType => "Table";
    public string Schema { get; }
    public List<ColumnNode> Columns { get; } = new();
    public List<ForeignKeyNode> ForeignKeys { get; } = new();
    public List<IndexNode> Indexes { get; } = new();
    public List<TriggerNode> Triggers { get; } = new();

    public TableNode(string schema, string name) : base(name)
    {
        Schema = schema ?? "public";
    }
}

/// <summary>
/// Represents a database view.
/// </summary>
public sealed class ViewNode : SchemaNode
{
    public override string NodeType => "View";
    public string Schema { get; }
    public List<ColumnNode> Columns { get; } = new();
    public List<TriggerNode> Triggers { get; } = new();

    public ViewNode(string schema, string name) : base(name)
    {
        Schema = schema ?? "public";
    }
}

/// <summary>
/// Represents a column in a table or view.
/// </summary>
public sealed class ColumnNode : SchemaNode
{
    public override string NodeType => "Column";
    public string DataType { get; }
    public bool IsNullable { get; }
    public string? DefaultValue { get; }
    public bool IsPrimaryKey { get; }

    public ColumnNode(
        string name,
        string dataType,
        bool isNullable = true,
        string? defaultValue = null,
        bool isPrimaryKey = false) : base(name)
    {
        DataType = dataType ?? "unknown";
        IsNullable = isNullable;
        DefaultValue = defaultValue;
        IsPrimaryKey = isPrimaryKey;
    }

    public string DisplayText => $"{Name} : {DataType}";
}

/// <summary>
/// Represents an index on a table.
/// </summary>
public sealed class IndexNode : SchemaNode
{
    public override string NodeType => "Index";
    public bool IsUnique { get; }
    public List<string> Columns { get; }

    public IndexNode(string name, bool isUnique, List<string> columns) : base(name)
    {
        IsUnique = isUnique;
        Columns = columns ?? new List<string>();
    }
}

/// <summary>
/// Represents a foreign key from a source column to a referenced table/column.
/// </summary>
public sealed class ForeignKeyNode : SchemaNode
{
    public override string NodeType => "ForeignKey";
    public string ColumnName { get; }
    public string ReferencedTable { get; }
    public string ReferencedColumn { get; }

    public ForeignKeyNode(string name, string columnName, string referencedTable, string referencedColumn)
        : base(name)
    {
        ColumnName = columnName;
        ReferencedTable = referencedTable;
        ReferencedColumn = referencedColumn;
    }

    public string DisplayText => $"{ColumnName} -> {ReferencedTable}({ReferencedColumn})";
}

/// <summary>
/// Represents a table/view trigger.
/// </summary>
public sealed class TriggerNode : SchemaNode
{
    public override string NodeType => "Trigger";
    public string Timing { get; }
    public string Event { get; }
    public string ParentObjectName { get; }
    public string? SourceSql { get; }

    public TriggerNode(string name, string timing, string triggerEvent, string parentObjectName, string? sourceSql = null)
        : base(name)
    {
        Timing = timing;
        Event = triggerEvent;
        ParentObjectName = parentObjectName;
        SourceSql = sourceSql;
    }
}
