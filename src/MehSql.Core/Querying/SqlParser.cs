using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MehSql.Core.Querying;

/// <summary>
/// Parses SQL text to extract table aliases and detect the editing context at a cursor position.
/// </summary>
public interface ISqlParser
{
    /// <summary>
    /// Analyzes the SQL around the cursor to determine what kind of suggestions to show.
    /// </summary>
    SqlContext GetContextAtPosition(string sql, int cursorPosition);

    /// <summary>
    /// Extracts all table aliases (e.g. FROM Artists a → a → Artists).
    /// </summary>
    Dictionary<string, string> ExtractTableAliases(string sql);
}

/// <inheritdoc />
public class SqlParser : ISqlParser
{
    private Dictionary<string, string>? _cachedAliases;
    private int? _lastQueryHash;

    public SqlContext GetContextAtPosition(string sql, int cursorPosition)
    {
        var context = new SqlContext();

        if (IsInStringLiteral(sql, cursorPosition) || IsInComment(sql, cursorPosition))
        {
            return context;
        }

        context.Aliases = ExtractTableAliases(sql);

        // Check if user typed "alias.", "alias.partial", or "alias."partial" (quoted column)
        if (cursorPosition > 1 && cursorPosition <= sql.Length)
        {
            // Walk backward past any partial word to find a potential dot
            int pos = cursorPosition;
            while (pos > 0 && (char.IsLetterOrDigit(sql[pos - 1]) || sql[pos - 1] == '_'))
            {
                pos--;
            }

            // Skip opening double-quote for quoted column identifiers (e.g. alias."col)
            if (pos > 0 && sql[pos - 1] == '"')
            {
                pos--;
            }

            if (pos > 0 && sql[pos - 1] == '.')
            {
                var alias = ExtractIdentifierBeforeDot(sql, pos - 1);
                if (!string.IsNullOrEmpty(alias) && context.Aliases.ContainsKey(alias))
                {
                    context.CurrentAlias = alias;
                }
            }
        }

        context.CurrentClause = DetectClauseType(sql, cursorPosition);
        context.PartialWord = ExtractPartialWord(sql, cursorPosition);

        return context;
    }

    public Dictionary<string, string> ExtractTableAliases(string sql)
    {
        var queryHash = GetFromClauseHash(sql);
        if (_lastQueryHash == queryHash && _cachedAliases is not null)
        {
            return _cachedAliases;
        }

        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Match: FROM table_name [AS] alias (with optional double-quoted identifier)
        var fromPattern = @"\bFROM\s+""?(\w+)""?(?:\s+AS\s+|\s+)(\w+)";
        foreach (Match match in Regex.Matches(sql, fromPattern, RegexOptions.IgnoreCase))
        {
            var tableName = match.Groups[1].Value;
            var alias = match.Groups[2].Value;

            if (!IsKeyword(alias))
            {
                aliases[alias] = tableName;
            }
        }

        // Match: JOIN table_name [AS] alias (with optional double-quoted identifier)
        var joinPattern = @"\b(?:LEFT\s+|RIGHT\s+|INNER\s+|OUTER\s+)?JOIN\s+""?(\w+)""?(?:\s+AS\s+|\s+)(\w+)";
        foreach (Match match in Regex.Matches(sql, joinPattern, RegexOptions.IgnoreCase))
        {
            var tableName = match.Groups[1].Value;
            var alias = match.Groups[2].Value;

            if (!IsKeyword(alias))
            {
                aliases[alias] = tableName;
            }
        }

        _cachedAliases = aliases;
        _lastQueryHash = queryHash;
        return aliases;
    }

    private int GetFromClauseHash(string sql)
    {
        var fromMatch = Regex.Match(sql, @"FROM.*?(?:WHERE|ORDER|GROUP|LIMIT|$)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return fromMatch.Value.GetHashCode();
    }

    private string ExtractIdentifierBeforeDot(string sql, int dotPosition)
    {
        int start = dotPosition - 1;
        while (start >= 0 && (char.IsLetterOrDigit(sql[start]) || sql[start] == '_'))
        {
            start--;
        }
        return sql.Substring(start + 1, dotPosition - start - 1);
    }

    private bool IsInStringLiteral(string sql, int position)
    {
        int quoteCount = 0;
        for (int i = 0; i < position && i < sql.Length; i++)
        {
            if (sql[i] == '\'' && (i == 0 || sql[i - 1] != '\\'))
            {
                quoteCount++;
            }
        }
        return quoteCount % 2 == 1;
    }

    private bool IsInComment(string sql, int position)
    {
        var lineStart = sql.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
        var lineText = sql.Substring(lineStart, position - lineStart);
        return lineText.Contains("--");
    }

    private SqlClauseType DetectClauseType(string sql, int cursorPosition)
    {
        var beforeCursor = sql.Substring(0, cursorPosition);

        var opts = RegexOptions.IgnoreCase | RegexOptions.Singleline;

        if (Regex.IsMatch(beforeCursor, @"\bWHERE\b(?!.*\b(?:ORDER|GROUP|LIMIT)\b)", opts))
            return SqlClauseType.Where;
        if (Regex.IsMatch(beforeCursor, @"\bSELECT\b(?!.*\bFROM\b)", opts))
            return SqlClauseType.Select;
        if (Regex.IsMatch(beforeCursor, @"\bFROM\b(?!.*\bWHERE\b)", opts))
            return SqlClauseType.From;

        return SqlClauseType.Unknown;
    }

    private string ExtractPartialWord(string sql, int cursorPosition)
    {
        int start = cursorPosition;
        while (start > 0 && (char.IsLetterOrDigit(sql[start - 1]) || sql[start - 1] == '_'))
        {
            start--;
        }
        return sql.Substring(start, cursorPosition - start);
    }

    private bool IsKeyword(string word)
    {
        return Array.Exists(SqlKeywords.All, k =>
            k.Equals(word, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Describes the editing context at a cursor position in SQL text.
/// </summary>
public class SqlContext
{
    public SqlClauseType CurrentClause { get; set; }
    public Dictionary<string, string> Aliases { get; set; } = new();
    public string? CurrentAlias { get; set; }
    public string PartialWord { get; set; } = string.Empty;

    /// <summary>
    /// Returns true when the structural context changed (clause or alias),
    /// meaning cached candidates should be regenerated.
    /// </summary>
    public bool IsMaterialChange(SqlContext other)
    {
        return CurrentClause != other.CurrentClause || CurrentAlias != other.CurrentAlias;
    }
}

public enum SqlClauseType
{
    Unknown,
    Select,
    From,
    Where,
    OrderBy
}
