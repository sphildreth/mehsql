using System.Text.RegularExpressions;

namespace MehSql.Core.Querying;

public static class SimpleSqlFormatter
{
    private static readonly (string Pattern, string Replacement)[] Rules =
    [
        (@"\bSELECT\b", "SELECT"),
        (@"\bFROM\b", "\nFROM"),
        (@"\bWHERE\b", "\nWHERE"),
        (@"\bGROUP\s+BY\b", "\nGROUP BY"),
        (@"\bORDER\s+BY\b", "\nORDER BY"),
        (@"\bLIMIT\b", "\nLIMIT"),
        (@"\bOFFSET\b", " OFFSET"),
        (@"\bINNER\s+JOIN\b", "\nINNER JOIN"),
        (@"\bLEFT\s+JOIN\b", "\nLEFT JOIN"),
        (@"\bRIGHT\s+JOIN\b", "\nRIGHT JOIN"),
        (@"\bFULL\s+JOIN\b", "\nFULL JOIN"),
        (@"\bCROSS\s+JOIN\b", "\nCROSS JOIN"),
        (@"\bJOIN\b", "\nJOIN"),
        (@"\bHAVING\b", "\nHAVING"),
        (@"\bVALUES\b", "\nVALUES"),
        (@"\bSET\b", "\nSET"),
        (@"\bAND\b", "\n  AND"),
        (@"\bOR\b", "\n  OR")
    ];

    public static string Format(string sql)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return string.Empty;
        }

        var normalized = Regex.Replace(sql.Trim(), @"\s+", " ");
        foreach (var (pattern, replacement) in Rules)
        {
            normalized = Regex.Replace(
                normalized,
                pattern,
                replacement,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return normalized.Trim();
    }
}
