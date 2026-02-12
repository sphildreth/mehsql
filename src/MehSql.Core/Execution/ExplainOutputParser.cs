using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MehSql.Core.Execution;

/// <summary>
/// Parses EXPLAIN/EXPLAIN ANALYZE text output for summary timing values.
/// </summary>
internal static partial class ExplainOutputParser
{
    public static (double? PlanningTime, double? ExecutionTime) ParseTimings(string? rawOutput)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return (null, null);
        }

        var planning = TryParseTiming(rawOutput, PlanningTimeRegex());
        var execution = TryParseTiming(rawOutput, ExecutionTimeRegex());

        return (planning, execution);
    }

    private static double? TryParseTiming(string rawOutput, Regex regex)
    {
        var match = regex.Match(rawOutput);
        if (!match.Success)
        {
            return null;
        }

        var valueText = match.Groups["value"].Value;
        if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return null;
    }

    [GeneratedRegex(@"\bPlanning\s+Time\b\s*[:=]?\s*(?<value>\d+(?:\.\d+)?)\s*ms", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PlanningTimeRegex();

    [GeneratedRegex(@"\bExecution\s+Time\b\s*[:=]?\s*(?<value>\d+(?:\.\d+)?)\s*ms", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExecutionTimeRegex();
}
