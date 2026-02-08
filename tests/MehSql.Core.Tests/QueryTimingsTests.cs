using System;
using MehSql.Core.Querying;
using Xunit;

namespace MehSql.Core.Tests;

public class QueryTimingsTests
{
    [Fact]
    public void TotalTime_WithAllValues_SumsCorrectly()
    {
        var timings = new QueryTimings(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(5)
        );

        Assert.Equal(TimeSpan.FromMilliseconds(35), timings.TotalTime);
    }

    [Fact]
    public void TotalTime_WithNullDbExecutionTime_SumsCorrectly()
    {
        var timings = new QueryTimings(
            null,
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(5)
        );

        Assert.Equal(TimeSpan.FromMilliseconds(25), timings.TotalTime);
    }

    [Fact]
    public void TotalTime_WithNullUiBindTime_SumsCorrectly()
    {
        var timings = new QueryTimings(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(20),
            null
        );

        Assert.Equal(TimeSpan.FromMilliseconds(30), timings.TotalTime);
    }

    [Fact]
    public void TotalTime_WithAllNullExceptFetch_UsesFetchOnly()
    {
        var timings = new QueryTimings(
            null,
            TimeSpan.FromMilliseconds(20),
            null
        );

        Assert.Equal(TimeSpan.FromMilliseconds(20), timings.TotalTime);
    }
}
