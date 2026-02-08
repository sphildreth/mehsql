using System;
using System.Collections.Generic;
using System.Text.Json;

namespace MehSql.Core.Execution;

/// <summary>
/// Represents a single node in a query execution plan.
/// </summary>
public sealed class PlanNode
{
    public string NodeType { get; set; } = "";
    public string? RelationName { get; set; }
    public string? Schema { get; set; }
    public string? IndexName { get; set; }
    public string? JoinType { get; set; }
    public double? StartupCost { get; set; }
    public double? TotalCost { get; set; }
    public long? PlanRows { get; set; }
    public int? PlanWidth { get; set; }
    public double? ActualStartupTime { get; set; }
    public double? ActualTotalTime { get; set; }
    public long? ActualRows { get; set; }
    public long? ActualLoops { get; set; }
    public string? Output { get; set; }
    public string? Filter { get; set; }
    public List<PlanNode> Plans { get; } = new();
}

/// <summary>
/// Complete query execution plan with timing information.
/// </summary>
public sealed class QueryExecutionPlan
{
    public PlanNode Plan { get; set; } = new();
    public double? PlanningTime { get; set; }
    public double? ExecutionTime { get; set; }
    public string RawOutput { get; set; } = "";
    public bool IsAnalyzed { get; set; }
}

/// <summary>
/// Service for executing EXPLAIN ANALYZE queries.
/// </summary>
public interface IExplainService
{
    /// <summary>
    /// Gets the execution plan for a SQL query without running it.
    /// </summary>
    Task<QueryExecutionPlan> ExplainAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// Executes the query and gets actual timing information.
    /// </summary>
    Task<QueryExecutionPlan> ExplainAnalyzeAsync(string sql, CancellationToken ct = default);
}
