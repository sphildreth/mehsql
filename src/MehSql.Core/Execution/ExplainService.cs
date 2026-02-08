using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MehSql.Core.Connections;
using MehSql.Core.Execution;

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

        // Build EXPLAIN query
        var explainPrefix = analyze ? "EXPLAIN (ANALYZE, FORMAT JSON)" : "EXPLAIN (FORMAT JSON)";
        var explainSql = $"{explainPrefix} {sql}";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = explainSql;

        var rawOutput = "";
        try
        {
            using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
            {
                rawOutput = reader.GetString(0);
            }
        }
        catch (Exception ex)
        {
            // DecentDB may not support EXPLAIN ANALYZE
            // Return a placeholder with the error message
            return new QueryExecutionPlan
            {
                RawOutput = $"EXPLAIN not supported: {ex.Message}",
                IsAnalyzed = analyze,
                PlanningTime = null,
                ExecutionTime = null
            };
        }

        // Try to parse the JSON output
        try
        {
            return ParseExplainOutput(rawOutput, analyze);
        }
        catch
        {
            // If parsing fails, return raw output
            return new QueryExecutionPlan
            {
                RawOutput = rawOutput,
                IsAnalyzed = analyze
            };
        }
    }

    private static QueryExecutionPlan ParseExplainOutput(string json, bool isAnalyzed)
    {
        var plan = new QueryExecutionPlan
        {
            RawOutput = json,
            IsAnalyzed = isAnalyzed
        };

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // PostgreSQL EXPLAIN FORMAT JSON returns an array with one element
            if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0)
            {
                root = root[0];
            }

            if (root.TryGetProperty("Plan", out var planElement))
            {
                plan.Plan = ParsePlanNode(planElement);
            }

            if (root.TryGetProperty("Planning Time", out var planningTimeElement))
            {
                plan.PlanningTime = planningTimeElement.GetDouble();
            }

            if (root.TryGetProperty("Execution Time", out var executionTimeElement))
            {
                plan.ExecutionTime = executionTimeElement.GetDouble();
            }
        }
        catch
        {
            // Leave plan empty if parsing fails
        }

        return plan;
    }

    private static PlanNode ParsePlanNode(JsonElement element)
    {
        var node = new PlanNode();

        if (element.TryGetProperty("Node Type", out var nodeType))
        {
            node.NodeType = nodeType.GetString() ?? "";
        }

        if (element.TryGetProperty("Relation Name", out var relationName))
        {
            node.RelationName = relationName.GetString();
        }

        if (element.TryGetProperty("Schema", out var schema))
        {
            node.Schema = schema.GetString();
        }

        if (element.TryGetProperty("Index Name", out var indexName))
        {
            node.IndexName = indexName.GetString();
        }

        if (element.TryGetProperty("Join Type", out var joinType))
        {
            node.JoinType = joinType.GetString();
        }

        if (element.TryGetProperty("Startup Cost", out var startupCost))
        {
            node.StartupCost = startupCost.GetDouble();
        }

        if (element.TryGetProperty("Total Cost", out var totalCost))
        {
            node.TotalCost = totalCost.GetDouble();
        }

        if (element.TryGetProperty("Plan Rows", out var planRows))
        {
            node.PlanRows = planRows.GetInt64();
        }

        if (element.TryGetProperty("Plan Width", out var planWidth))
        {
            node.PlanWidth = planWidth.GetInt32();
        }

        if (element.TryGetProperty("Actual Startup Time", out var actualStartupTime))
        {
            node.ActualStartupTime = actualStartupTime.GetDouble();
        }

        if (element.TryGetProperty("Actual Total Time", out var actualTotalTime))
        {
            node.ActualTotalTime = actualTotalTime.GetDouble();
        }

        if (element.TryGetProperty("Actual Rows", out var actualRows))
        {
            node.ActualRows = actualRows.GetInt64();
        }

        if (element.TryGetProperty("Actual Loops", out var actualLoops))
        {
            node.ActualLoops = actualLoops.GetInt64();
        }

        if (element.TryGetProperty("Output", out var output))
        {
            node.Output = output.GetString();
        }

        if (element.TryGetProperty("Filter", out var filter))
        {
            node.Filter = filter.GetString();
        }

        // Parse child plans
        if (element.TryGetProperty("Plans", out var plans))
        {
            foreach (var childPlan in plans.EnumerateArray())
            {
                node.Plans.Add(ParsePlanNode(childPlan));
            }
        }

        return node;
    }
}
