using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MehSql.Core.Connections;

namespace MehSql.Core.Execution;

/// <summary>
/// Service for executing EXPLAIN ANALYZE queries and parsing results.
/// </summary>
public sealed class ExplainService : IExplainService
{
    private readonly IConnectionFactory _connectionFactory;

    public ExplainService(IConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<QueryExecutionPlan> ExplainAsync(string sql, CancellationToken ct = default)
    {
        return await ExecuteExplainAsync(sql, analyze: false, ct);
    }

    public async Task<QueryExecutionPlan> ExplainAnalyzeAsync(string sql, CancellationToken ct = default)
    {
        return await ExecuteExplainAsync(sql, analyze: true, ct);
    }

    private async Task<QueryExecutionPlan> ExecuteExplainAsync(string sql, bool analyze, CancellationToken ct)
    {
        using var conn = _connectionFactory.CreateConnection();
        await conn.OpenAsync(ct);

        // Build EXPLAIN query — DecentDB supports plain EXPLAIN but not FORMAT options
        var explainPrefix = analyze ? "EXPLAIN ANALYZE" : "EXPLAIN";
        var cleanSql = sql.TrimEnd().TrimEnd(';');
        var explainSql = $"{explainPrefix} {cleanSql}";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = explainSql;

        var rawOutput = "";
        try
        {
            using var reader = await cmd.ExecuteReaderAsync(ct);
            var lines = new List<string>();
            while (await reader.ReadAsync(ct))
            {
                lines.Add(reader.GetString(0));
            }
            rawOutput = string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            return new QueryExecutionPlan
            {
                RawOutput = $"EXPLAIN failed: {ex.Message}",
                IsAnalyzed = analyze,
                PlanningTime = null,
                ExecutionTime = null
            };
        }

        // Return the raw text output — DecentDB returns plain text, not JSON
        return new QueryExecutionPlan
        {
            RawOutput = rawOutput,
            IsAnalyzed = analyze
        };
    }
}
