using MehSql.Core.Querying;
using Xunit;

namespace MehSql.Core.Tests;

public sealed class SimpleSqlFormatterTests
{
    [Fact]
    public void Format_AddsLineBreaksForCommonClauses()
    {
        var sql = "select id,name from users where active = true order by id limit 10";

        var formatted = SimpleSqlFormatter.Format(sql);

        Assert.Contains("\nFROM", formatted);
        Assert.Contains("\nWHERE", formatted);
        Assert.Contains("\nORDER BY", formatted);
        Assert.Contains("\nLIMIT", formatted);
    }
}
