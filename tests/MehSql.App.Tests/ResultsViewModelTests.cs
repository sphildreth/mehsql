using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MehSql.App.ViewModels;
using MehSql.Core.Querying;
using Moq;
using Xunit;

namespace MehSql.App.Tests;

public sealed class ResultsViewModelTests
{
    [Fact]
    public async Task RunAsync_ClearsRows_ThenLoadsFirstPage()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Strict);

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

        var vm = new ResultsViewModel(pager.Object);
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

        var vm = new ResultsViewModel(pager.Object);

        await vm.RunAsync(CancellationToken.None);
        Assert.Single(vm.Rows);

        await vm.LoadMoreAsync(CancellationToken.None);
        Assert.Equal(3, vm.Rows.Count);
    }

    [Fact]
    public async Task RunAsync_WithoutOrderBy_SetsOrderingWarning()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Strict);
        pager.Setup(p => p.ExecuteFirstPageAsync(It.IsAny<string>(), It.IsAny<QueryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new QueryPage(
                 Columns: new[] { new ColumnInfo("id", "bigint") },
                 Rows: new List<IReadOnlyDictionary<string, object?>>(),
                 NextToken: null,
                 Timings: new QueryTimings(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(2), null)
             ));

        var vm = new ResultsViewModel(pager.Object);
        vm.Sql = "SELECT * FROM users";

        await vm.RunAsync(CancellationToken.None);

        Assert.True(vm.HasOrderingWarning);
    }

    [Fact]
    public async Task RunAsync_WithOrderBy_ClearsOrderingWarning()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Strict);
        pager.Setup(p => p.ExecuteFirstPageAsync(It.IsAny<string>(), It.IsAny<QueryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new QueryPage(
                 Columns: new[] { new ColumnInfo("id", "bigint") },
                 Rows: new List<IReadOnlyDictionary<string, object?>>(),
                 NextToken: null,
                 Timings: new QueryTimings(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(2), null)
             ));

        var vm = new ResultsViewModel(pager.Object);
        vm.Sql = "SELECT * FROM users ORDER BY id";

        await vm.RunAsync(CancellationToken.None);

        Assert.False(vm.HasOrderingWarning);
    }

    [Fact]
    public async Task RunAsync_WithOrderByLowerCase_ClearsOrderingWarning()
    {
        var pager = new Mock<IQueryPager>(MockBehavior.Strict);
        pager.Setup(p => p.ExecuteFirstPageAsync(It.IsAny<string>(), It.IsAny<QueryOptions>(), It.IsAny<CancellationToken>()))
             .ReturnsAsync(new QueryPage(
                 Columns: new[] { new ColumnInfo("id", "bigint") },
                 Rows: new List<IReadOnlyDictionary<string, object?>>(),
                 NextToken: null,
                 Timings: new QueryTimings(TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(2), null)
             ));

        var vm = new ResultsViewModel(pager.Object);
        vm.Sql = "select * from users order by name";

        await vm.RunAsync(CancellationToken.None);

        Assert.False(vm.HasOrderingWarning);
    }
}
