using MehSql.Core.Execution;
using Xunit;

namespace MehSql.Core.Tests;

public sealed class ExplainOutputParserTests
{
    [Fact]
    public void ParseTimings_ReturnsNulls_WhenOutputIsEmpty()
    {
        var timings = ExplainOutputParser.ParseTimings(string.Empty);

        Assert.Null(timings.PlanningTime);
        Assert.Null(timings.ExecutionTime);
    }

    [Fact]
    public void ParseTimings_ParsesPostgresStyleTimingLines()
    {
        const string output = """
Seq Scan on users  (cost=0.00..22.70 rows=1270 width=36) (actual time=0.011..0.163 rows=1270 loops=1)
Planning Time: 0.123 ms
Execution Time: 1.456 ms
""";

        var timings = ExplainOutputParser.ParseTimings(output);

        Assert.Equal(0.123, timings.PlanningTime);
        Assert.Equal(1.456, timings.ExecutionTime);
    }

    [Fact]
    public void ParseTimings_ParsesCaseInsensitiveAssignments()
    {
        const string output = """
planning time = 2.5 ms
execution time = 9.75 ms
""";

        var timings = ExplainOutputParser.ParseTimings(output);

        Assert.Equal(2.5, timings.PlanningTime);
        Assert.Equal(9.75, timings.ExecutionTime);
    }
}
