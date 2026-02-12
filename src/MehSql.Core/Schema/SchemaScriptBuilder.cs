using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MehSql.Core.Schema;

public static class SchemaScriptBuilder
{
    public static string BuildTableDdl(TableNode table, bool includeDrop = false)
    {
        var lines = new List<string>
        {
            "-- MehSQL synthesized DDL",
            $"-- Generated at {DateTimeOffset.UtcNow:O}"
        };

        if (includeDrop)
        {
            lines.Add($"DROP TABLE IF EXISTS {QuoteIdentifier(table.Name)};");
            lines.Add(string.Empty);
        }

        var columnDefs = new List<string>();
        foreach (var column in table.Columns)
        {
            var parts = new List<string> { $"{QuoteIdentifier(column.Name)} {column.DataType}" };
            if (!column.IsNullable)
            {
                parts.Add("NOT NULL");
            }

            if (column.IsPrimaryKey)
            {
                parts.Add("PRIMARY KEY");
            }

            if (!string.IsNullOrWhiteSpace(column.DefaultValue))
            {
                parts.Add($"DEFAULT {column.DefaultValue}");
            }

            columnDefs.Add($"    {string.Join(" ", parts)}");
        }

        lines.Add($"CREATE TABLE {QuoteIdentifier(table.Name)} (");
        lines.Add(string.Join(",\n", columnDefs));
        lines.Add(");");

        foreach (var index in table.Indexes)
        {
            var unique = index.IsUnique ? "UNIQUE " : string.Empty;
            var columns = string.Join(", ", index.Columns.Select(QuoteIdentifier));
            lines.Add($"CREATE {unique}INDEX {QuoteIdentifier(index.Name)} ON {QuoteIdentifier(table.Name)} ({columns});");
        }

        if (table.ForeignKeys.Count > 0 || table.Triggers.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("-- Notes: foreign keys/triggers are shown in schema explorer but may be incomplete in synthesized DDL.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildViewDdl(ViewNode view)
    {
        var lines = new List<string>
        {
            "-- MehSQL synthesized DDL",
            $"-- Generated at {DateTimeOffset.UtcNow:O}",
            "-- Original view source is unavailable from current DecentDB introspection.",
            $"-- Replace SELECT body as needed for {view.Name}.",
            $"CREATE VIEW {QuoteIdentifier(view.Name)} AS",
            "SELECT 1 AS placeholder;"
        };

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildTriggerDdl(TriggerNode trigger)
    {
        var source = string.IsNullOrWhiteSpace(trigger.SourceSql)
            ? "-- Trigger source SQL unavailable from introspection."
            : trigger.SourceSql;

        return string.Join(Environment.NewLine, new[]
        {
            "-- MehSQL best-effort trigger DDL",
            $"-- Trigger: {trigger.Name}",
            source
        });
    }

    public static string BuildCrudTemplates(TableNode table)
    {
        var columns = table.Columns.Select(c => c.Name).ToList();
        if (columns.Count == 0)
        {
            return $"-- No columns found for table {table.Name}";
        }

        var quotedColumns = columns.Select(QuoteIdentifier).ToList();
        var insertColumns = string.Join(", ", quotedColumns);
        var insertValues = string.Join(", ", columns.Select((_, i) => $"${i + 1}"));
        var updateSet = string.Join(", ", columns.Select((c, i) => $"{QuoteIdentifier(c)} = ${i + 1}"));

        var primaryKey = table.Columns.FirstOrDefault(c => c.IsPrimaryKey)?.Name;
        var where = primaryKey is null ? "-- TODO: add WHERE clause" : $"{QuoteIdentifier(primaryKey)} = $1";

        var lines = new List<string>
        {
            $"-- CRUD templates for {table.Name}",
            $"INSERT INTO {QuoteIdentifier(table.Name)} ({insertColumns})",
            $"VALUES ({insertValues});",
            string.Empty,
            $"UPDATE {QuoteIdentifier(table.Name)}",
            $"SET {updateSet}",
            $"WHERE {where};",
            string.Empty,
            $"DELETE FROM {QuoteIdentifier(table.Name)}",
            $"WHERE {where};"
        };

        return string.Join(Environment.NewLine, lines);
    }

    public static string BuildDropStatement(string nodeType, string name)
    {
        var objectType = nodeType switch
        {
            "Table" => "TABLE",
            "View" => "VIEW",
            "Index" => "INDEX",
            "Trigger" => "TRIGGER",
            "Column" => "COLUMN",
            _ => "OBJECT"
        };

        return $"DROP {objectType} {QuoteIdentifier(name)};";
    }

    private static string QuoteIdentifier(string name) => $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
}
