using System;
using System.Collections.Generic;

namespace MehSql.Core.Querying;

public enum SqlExecutionTarget
{
    Selection,
    CurrentStatement,
    WholeEditor
}

public sealed record SqlExecutionRequest(string Sql, SqlExecutionTarget Target);

public static class SqlExecutionPlanner
{
    public static SqlExecutionRequest Create(string? editorText, string? selectedText, int caretIndex)
    {
        var sql = editorText ?? string.Empty;
        var selected = selectedText?.Trim();
        if (!string.IsNullOrWhiteSpace(selected))
        {
            return new SqlExecutionRequest(selected, SqlExecutionTarget.Selection);
        }

        var statement = ExtractCurrentStatement(sql, caretIndex);
        if (!string.IsNullOrWhiteSpace(statement))
        {
            return new SqlExecutionRequest(statement, SqlExecutionTarget.CurrentStatement);
        }

        return new SqlExecutionRequest(sql.Trim(), SqlExecutionTarget.WholeEditor);
    }

    internal static string ExtractCurrentStatement(string sql, int caretIndex)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return string.Empty;
        }

        var normalizedCaret = Math.Clamp(caretIndex, 0, sql.Length);
        var statement = ExtractAtPosition(sql, normalizedCaret);
        if (!string.IsNullOrWhiteSpace(statement))
        {
            return statement;
        }

        if (normalizedCaret > 0)
        {
            statement = ExtractAtPosition(sql, normalizedCaret - 1);
            if (!string.IsNullOrWhiteSpace(statement))
            {
                return statement;
            }
        }

        if (normalizedCaret < sql.Length)
        {
            statement = ExtractAtPosition(sql, normalizedCaret + 1);
            if (!string.IsNullOrWhiteSpace(statement))
            {
                return statement;
            }
        }

        return string.Empty;
    }

    private static string ExtractAtPosition(string sql, int position)
    {
        var delimiters = FindStatementDelimiters(sql);

        var start = 0;
        foreach (var delimiter in delimiters)
        {
            if (delimiter < position)
            {
                start = delimiter + 1;
                continue;
            }

            break;
        }

        var end = sql.Length;
        foreach (var delimiter in delimiters)
        {
            if (delimiter >= position)
            {
                end = delimiter;
                break;
            }
        }

        if (end <= start)
        {
            return string.Empty;
        }

        return sql[start..end].Trim();
    }

    private static List<int> FindStatementDelimiters(string sql)
    {
        var delimiters = new List<int>();
        var inSingleQuote = false;
        var inDoubleQuote = false;
        var inLineComment = false;
        var inBlockComment = false;

        for (var i = 0; i < sql.Length; i++)
        {
            var current = sql[i];
            var next = i + 1 < sql.Length ? sql[i + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inSingleQuote)
            {
                if (current == '\'' && next == '\'')
                {
                    i++;
                    continue;
                }

                if (current == '\'')
                {
                    inSingleQuote = false;
                }

                continue;
            }

            if (inDoubleQuote)
            {
                if (current == '"')
                {
                    inDoubleQuote = false;
                }

                continue;
            }

            if (current == '-' && next == '-')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (current == '\'')
            {
                inSingleQuote = true;
                continue;
            }

            if (current == '"')
            {
                inDoubleQuote = true;
                continue;
            }

            if (current == ';')
            {
                delimiters.Add(i);
            }
        }

        return delimiters;
    }
}
