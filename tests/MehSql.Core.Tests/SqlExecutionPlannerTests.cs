using MehSql.Core.Querying;
using Xunit;

namespace MehSql.Core.Tests;

public sealed class SqlExecutionPlannerTests
{
    [Fact]
    public void Create_UsesSelection_WhenPresent()
    {
        var request = SqlExecutionPlanner.Create(
            "SELECT * FROM users; SELECT * FROM posts;",
            "  SELECT * FROM posts  ",
            caretIndex: 2);

        Assert.Equal("SELECT * FROM posts", request.Sql);
        Assert.Equal(SqlExecutionTarget.Selection, request.Target);
    }

    [Fact]
    public void Create_UsesCurrentStatement_WhenNoSelection()
    {
        var sql = "SELECT * FROM users; SELECT * FROM posts ORDER BY id;";
        var caret = sql.IndexOf("posts", System.StringComparison.Ordinal);
        var request = SqlExecutionPlanner.Create(sql, null, caret);

        Assert.Equal("SELECT * FROM posts ORDER BY id", request.Sql);
        Assert.Equal(SqlExecutionTarget.CurrentStatement, request.Target);
    }

    [Fact]
    public void Create_IgnoresSemicolonsInsideStrings()
    {
        var sql = "SELECT ';not a delimiter' AS value; SELECT 2;";
        var caret = sql.IndexOf("value", System.StringComparison.Ordinal);
        var request = SqlExecutionPlanner.Create(sql, null, caret);

        Assert.Equal("SELECT ';not a delimiter' AS value", request.Sql);
        Assert.Equal(SqlExecutionTarget.CurrentStatement, request.Target);
    }

    [Fact]
    public void Create_ClampsCaretAndUsesCurrentStatement()
    {
        var request = SqlExecutionPlanner.Create("   SELECT 1   ", null, caretIndex: 999);

        Assert.Equal("SELECT 1", request.Sql);
        Assert.Equal(SqlExecutionTarget.CurrentStatement, request.Target);
    }
}
