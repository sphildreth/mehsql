using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace MehSql.App.Controls;

/// <summary>
/// Simple SQL syntax highlighter
/// </summary>
public static class SqlSyntaxHighlighter
{
    private static readonly string[] SqlKeywords =
    [
        "SELECT", "FROM", "WHERE", "INSERT", "UPDATE", "DELETE", "CREATE", "DROP", "ALTER",
        "TABLE", "VIEW", "INDEX", "JOIN", "INNER", "LEFT", "RIGHT", "OUTER", "FULL", "CROSS",
        "ON", "AND", "OR", "NOT", "IN", "EXISTS", "BETWEEN", "LIKE", "IS", "NULL",
        "ORDER", "BY", "GROUP", "HAVING", "DISTINCT", "UNION", "ALL", "LIMIT", "OFFSET",
        "AS", "INTO", "VALUES", "SET", "PRIMARY", "KEY", "FOREIGN", "REFERENCES",
        "DEFAULT", "CHECK", "UNIQUE", "CONSTRAINT", "CASCADE", "RESTRICT",
        "BEGIN", "COMMIT", "ROLLBACK", "TRANSACTION", "CASE", "WHEN", "THEN", "ELSE", "END",
        "WITH", "RECURSIVE", "OVER", "PARTITION", "WINDOW", "ROW_NUMBER", "RANK", "DENSE_RANK"
    ];

    private static readonly string[] SqlFunctions =
    [
        "COUNT", "SUM", "AVG", "MIN", "MAX", "CAST", "CONVERT", "SUBSTRING", "LENGTH",
        "UPPER", "LOWER", "TRIM", "REPLACE", "CONCAT", "COALESCE", "NULLIF",
        "DATE", "DATETIME", "TIMESTAMP", "NOW", "CURRENT_DATE", "CURRENT_TIME",
        "ABS", "ROUND", "FLOOR", "CEIL", "POWER", "SQRT"
    ];

    private static readonly HashSet<string> KeywordSet = new(SqlKeywords, StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> FunctionSet = new(SqlFunctions, StringComparer.OrdinalIgnoreCase);

    public static List<TextSegment> Highlight(string sql)
    {
        var segments = new List<TextSegment>();
        if (string.IsNullOrEmpty(sql))
            return segments;

        var pos = 0;
        while (pos < sql.Length)
        {
            if (char.IsWhiteSpace(sql[pos]))
            {
                var start = pos;
                while (pos < sql.Length && char.IsWhiteSpace(sql[pos]))
                    pos++;
                segments.Add(new TextSegment(sql[start..pos], TextSegmentType.Normal));
                continue;
            }

            if (pos < sql.Length - 1 && sql[pos] == '-' && sql[pos + 1] == '-')
            {
                var start = pos;
                while (pos < sql.Length && sql[pos] != '\n')
                    pos++;
                segments.Add(new TextSegment(sql[start..pos], TextSegmentType.Comment));
                continue;
            }

            if (pos < sql.Length - 1 && sql[pos] == '/' && sql[pos + 1] == '*')
            {
                var start = pos;
                pos += 2;
                while (pos < sql.Length - 1)
                {
                    if (sql[pos] == '*' && sql[pos + 1] == '/')
                    {
                        pos += 2;
                        break;
                    }
                    pos++;
                }
                segments.Add(new TextSegment(sql[start..pos], TextSegmentType.Comment));
                continue;
            }

            if (sql[pos] == '\'')
            {
                var start = pos;
                pos++;
                while (pos < sql.Length)
                {
                    if (sql[pos] == '\'')
                    {
                        pos++;
                        if (pos < sql.Length && sql[pos] == '\'')
                            pos++;
                        else
                            break;
                    }
                    else
                    {
                        pos++;
                    }
                }
                segments.Add(new TextSegment(sql[start..pos], TextSegmentType.String));
                continue;
            }

            if (char.IsDigit(sql[pos]))
            {
                var start = pos;
                while (pos < sql.Length && (char.IsDigit(sql[pos]) || sql[pos] == '.'))
                    pos++;
                segments.Add(new TextSegment(sql[start..pos], TextSegmentType.Number));
                continue;
            }

            if (char.IsLetter(sql[pos]) || sql[pos] == '_')
            {
                var start = pos;
                while (pos < sql.Length && (char.IsLetterOrDigit(sql[pos]) || sql[pos] == '_'))
                    pos++;
                
                var word = sql[start..pos];
                TextSegmentType type = TextSegmentType.Normal;
                
                if (KeywordSet.Contains(word))
                    type = TextSegmentType.Keyword;
                else if (FunctionSet.Contains(word))
                    type = TextSegmentType.Function;
                
                segments.Add(new TextSegment(word, type));
                continue;
            }

            segments.Add(new TextSegment(sql[pos].ToString(), TextSegmentType.Normal));
            pos++;
        }

        return segments;
    }

    public static IBrush GetBrush(TextSegmentType type, bool isDarkTheme = true)
    {
        if (isDarkTheme)
        {
            return type switch
            {
                TextSegmentType.Keyword => new SolidColorBrush(Color.Parse("#569CD6")),
                TextSegmentType.Function => new SolidColorBrush(Color.Parse("#DCDCAA")),
                TextSegmentType.String => new SolidColorBrush(Color.Parse("#CE9178")),
                TextSegmentType.Number => new SolidColorBrush(Color.Parse("#B5CEA8")),
                TextSegmentType.Comment => new SolidColorBrush(Color.Parse("#6A9955")),
                _ => new SolidColorBrush(Color.Parse("#D4D4D4"))
            };
        }
        else
        {
            return type switch
            {
                TextSegmentType.Keyword => new SolidColorBrush(Color.Parse("#0000FF")),
                TextSegmentType.Function => new SolidColorBrush(Color.Parse("#795E26")),
                TextSegmentType.String => new SolidColorBrush(Color.Parse("#A31515")),
                TextSegmentType.Number => new SolidColorBrush(Color.Parse("#098658")),
                TextSegmentType.Comment => new SolidColorBrush(Color.Parse("#008000")),
                _ => Brushes.Black
            };
        }
    }
}

public record TextSegment(string Text, TextSegmentType Type);

public enum TextSegmentType
{
    Normal,
    Keyword,
    Function,
    String,
    Number,
    Comment
}
