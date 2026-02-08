using System;
using System.IO;
using System.Threading.Tasks;
using MehSql.Core.Connections;
using MehSql.Core.Execution;
using Xunit;

namespace MehSql.Core.Tests;

/// <summary>
/// Tests for ExplainService.
/// Note: These tests verify the service structure. Full integration tests require DecentDB EXPLAIN support.
/// </summary>
public class ExplainServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly IConnectionFactory _connectionFactory;

    public ExplainServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"mehsql_explain_test_{Guid.NewGuid()}.db");
        _connectionFactory = new ConnectionFactory(_testDbPath);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_testDbPath))
            {
                File.Delete(_testDbPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task ExplainAsync_ReturnsPlan()
    {
        var service = new ExplainService(_connectionFactory);
        var plan = await service.ExplainAsync("SELECT 1", default);

        Assert.NotNull(plan);
        Assert.NotNull(plan.RawOutput);
        Assert.NotEmpty(plan.RawOutput);
        Assert.False(plan.IsAnalyzed);
    }

    [Fact]
    public async Task ExplainAnalyzeAsync_ReturnsPlan_WhenExplainNotSupported()
    {
        var service = new ExplainService(_connectionFactory);
        var plan = await service.ExplainAnalyzeAsync("SELECT 1", default);

        Assert.NotNull(plan);
        Assert.NotNull(plan.RawOutput);
        Assert.True(plan.IsAnalyzed);
    }

    [Fact]
    public void PlanNode_DefaultValues_AreCorrect()
    {
        var node = new PlanNode();
        Assert.Empty(node.NodeType);
        Assert.Empty(node.Plans);
    }

    [Fact]
    public void QueryExecutionPlan_DefaultValues_AreCorrect()
    {
        var plan = new QueryExecutionPlan();
        Assert.NotNull(plan.Plan);
        Assert.Empty(plan.RawOutput);
        Assert.False(plan.IsAnalyzed);
    }

    [Fact]
    public void PlanNode_WithValues_CanBeCreated()
    {
        var node = new PlanNode
        {
            NodeType = "Seq Scan",
            RelationName = "users",
            TotalCost = 100.5,
            PlanRows = 1000,
            ActualTotalTime = 5.2,
            ActualRows = 950
        };

        Assert.Equal("Seq Scan", node.NodeType);
        Assert.Equal("users", node.RelationName);
        Assert.Equal(100.5, node.TotalCost);
        Assert.Equal(1000, node.PlanRows);
        Assert.Equal(5.2, node.ActualTotalTime);
        Assert.Equal(950, node.ActualRows);
    }

    [Fact]
    public void QueryExecutionPlan_WithValues_CanBeCreated()
    {
        var plan = new QueryExecutionPlan
        {
            PlanningTime = 0.5,
            ExecutionTime = 10.2,
            IsAnalyzed = true,
            RawOutput = "{ \"Plan\": {} }"
        };

        Assert.Equal(0.5, plan.PlanningTime);
        Assert.Equal(10.2, plan.ExecutionTime);
        Assert.True(plan.IsAnalyzed);
        Assert.Equal("{ \"Plan\": {} }", plan.RawOutput);
    }
}
