using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MehSql.App.ViewModels;
using MehSql.Core.Execution;
using MehSql.Core.Export;
using MehSql.Core.Querying;
using Moq;
using Xunit;

namespace MehSql.App.Tests;

public sealed class ResultsViewModelTests
{
    private static Mock<IExplainService> CreateMockExplainService()
    {
        var mock = new Mock<IExplainService>(MockBehavior.Loose);
        mock.Setup(e => e.ExplainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecutionPlan
            {
                RawOutput = "Explain not supported",
                IsAnalyzed = false
            });
        mock.Setup(e => e.ExplainAnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new QueryExecutionPlan
            {
                RawOutput = "Explain not supported",
                IsAnalyzed = true
            });
        return mock;
    }

    private static Mock<IExportService> CreateMockExportService()
    {
        var mock = new Mock<IExportService>(MockBehavior.Loose);
        mock.Setup(e => e.ExportToCsvAsync(It.IsAny<IAsyncEnumerable<QueryPage>>(), It.IsAny<Stream>(), It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mock.Setup(e => e.ExportToJsonAsync(It.IsAny<IAsyncEnumerable<QueryPage>>(), It.IsAny<Stream>(), It.IsAny<ExportOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return mock;
    }

    [Fact]
    public async Task RunAsync_ClearsRows_ThenLoadsFirstPage()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Strict);
        var explainService = CreateMockExplainService();
        var exportService = CreateMockExportService();

        var first = new QueryPage(
            Columns: new[] { new ColumnInfo("id", "bigint") },
            Rows: new List<IReadOnlyDictionary<string, object?>> {
                new Dictionary<string, object?> { ["id"] = 1L },
                new Dictionary<string, object?> { ["id"] = 2L },
            },
            NextToken: new QueryPageToken("t1"),
            Timings: new QueryTimings(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(2), null)
        );

        pager.Setup(p => p.ExecuteFirstPageAsync(It.IsAny<string>(), It.IsAny<QueryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(first);

        var vm = new ResultsViewModel(pager.Object, explainService.Object, exportService.Object);
        vm.Rows.Add(new Dictionary<string, object?> { ["id"] = 999L });

        await vm.RunAsync(CancellationToken.None);

        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("id", vm.Columns[0].Name);
        Assert.NotNull(vm.Timings);
    }

    [Fact]
    public async Task LoadMoreAsync_AppendsRows_WhenTokenPresent()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Strict);
        var explainService = CreateMockExplainService();
        var exportService = CreateMockExportService();

        var first = new QueryPage(
            Columns: new[] { new ColumnInfo("id", "bigint") },
            Rows: new List<IReadOnlyDictionary<string, object?>> {
                new Dictionary<string, object?> { ["id"] = 1L },
            },
            NextToken: new QueryPageToken("t1"),
            Timings: new QueryTimings(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(2), null)
        );

        var next = new QueryPage(
            Columns: new[] { new ColumnInfo("id", "bigint") },
            Rows: new List<IReadOnlyDictionary<string, object?>> {
                new Dictionary<string, object?> { ["id"] = 2L },
                new Dictionary<string, object?> { ["id"] = 3L },
            },
            NextToken: null,
            Timings: new QueryTimings(null, TimeSpan.FromMilliseconds(3), null)
        );

        pager.Setup(p => p.ExecuteFirstPageAsync(It.IsAny<string>(), It.IsAny<QueryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(first);

        pager.Setup(p => p.ExecuteNextPageAsync(It.IsAny<string>(), It.IsAny<QueryOptions>(), It.Is<QueryPageToken>(t => t.Value == "t1"), It.IsAny<CancellationToken>()))
             .ReturnsAsync(next);

        var vm = new ResultsViewModel(pager.Object, explainService.Object, exportService.Object);

        await vm.RunAsync(CancellationToken.None);
        Assert.Single(vm.Rows);

        await vm.LoadMoreAsync(CancellationToken.None);
        Assert.Equal(3, vm.Rows.Count);
    }

    [Fact]
    public async Task RunAsync_WithoutOrderBy_SetsOrderingWarning()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Strict);
        var explainService = CreateMockExplainService();
        var exportService = CreateMockExportService();
        pager.Setup(p => p.ExecuteFirstPageAsync(It.IsAny<string>(), It.IsAny<QueryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new QueryPage(
                 Columns: new[] { new ColumnInfo("id", "bigint") },
                 Rows: new List<IReadOnlyDictionary<string, object?>>(),
                 NextToken: null,
                 Timings: new QueryTimings(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(2), null)
             ));

        var vm = new ResultsViewModel(pager.Object, explainService.Object, exportService.Object);
        vm.Sql = "SELECT * FROM users";

        await vm.RunAsync(CancellationToken.None);

        Assert.True(vm.HasOrderingWarning);
    }

    [Fact]
    public async Task RunAsync_WithOrderBy_ClearsOrderingWarning()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Strict);
        var explainService = CreateMockExplainService();
        var exportService = CreateMockExportService();
        pager.Setup(p => p.ExecuteFirstPageAsync(It.IsAny<string>(), It.IsAny<QueryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new QueryPage(
                 Columns: new[] { new ColumnInfo("id", "bigint") },
                 Rows: new List<IReadOnlyDictionary<string, object?>>(),
                 NextToken: null,
                 Timings: new QueryTimings(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(2), null)
             ));

        var vm = new ResultsViewModel(pager.Object, explainService.Object, exportService.Object);
        vm.Sql = "SELECT * FROM users ORDER BY id";

        await vm.RunAsync(CancellationToken.None);

        Assert.False(vm.HasOrderingWarning);
    }

    [Fact]
    public async Task RunAsync_WithOrderByLowerCase_ClearsOrderingWarning()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Strict);
        var explainService = CreateMockExplainService();
        var exportService = CreateMockExportService();
        pager.Setup(p => p.ExecuteFirstPageAsync(It.IsAny<string>(), It.IsAny<QueryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new QueryPage(
                 Columns: new[] { new ColumnInfo("id", "bigint") },
                 Rows: new List<IReadOnlyDictionary<string, object?>>(),
                 NextToken: null,
                 Timings: new QueryTimings(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(2), null)
             ));

        var vm = new ResultsViewModel(pager.Object, explainService.Object, exportService.Object);
        vm.Sql = "select * from users order by name";

        await vm.RunAsync(CancellationToken.None);

        Assert.False(vm.HasOrderingWarning);
    }

    [Fact]
    public async Task ExplainQueryCommand_SetsExecutionPlan()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Loose);
        var explainService = new Mock<IExplainService>(MockBehavior.Strict);
        var exportService = CreateMockExportService();
        explainService.Setup(e => e.ExplainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new QueryExecutionPlan
             {
                 RawOutput = "Seq Scan on users",
                 IsAnalyzed = false
             });

        var vm = new ResultsViewModel(pager.Object, explainService.Object, exportService.Object);
        vm.Sql = "SELECT * FROM users";

        Assert.False(vm.ShowExecutionPlan);
        await vm.ExplainQueryAsync(CancellationToken.None);

        Assert.True(vm.ShowExecutionPlan);
        Assert.NotNull(vm.ExecutionPlan);
        Assert.Equal("Seq Scan on users", vm.ExecutionPlan.RawOutput);
    }

    [Fact]
    public async Task ExplainAnalyzeCommand_SetsExecutionPlanAndRunsQuery()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Strict);
        var explainService = new Mock<IExplainService>(MockBehavior.Strict);
        var exportService = CreateMockExportService();

        explainService.Setup(e => e.ExplainAnalyzeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new QueryExecutionPlan
             {
                 RawOutput = "Seq Scan on users (actual time=0.1..0.2)",
                 IsAnalyzed = true,
                 PlanningTime = 0.1,
                 ExecutionTime = 0.2
             });

        pager.Setup(p => p.ExecuteFirstPageAsync(It.IsAny<string>(), It.IsAny<QueryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new QueryPage(
                 Columns: new[] { new ColumnInfo("id", "bigint") },
                 Rows: new List<IReadOnlyDictionary<string, object?>>(),
                 NextToken: null,
                 Timings: new QueryTimings(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(2), null)
             ));

        var vm = new ResultsViewModel(pager.Object, explainService.Object, exportService.Object);
        vm.Sql = "SELECT * FROM users";

        Assert.False(vm.ShowExecutionPlan);
        await vm.ExplainAnalyzeAsync(CancellationToken.None);

        Assert.True(vm.ShowExecutionPlan);
        Assert.NotNull(vm.ExecutionPlan);
        Assert.True(vm.ExecutionPlan.IsAnalyzed);
    }
}
