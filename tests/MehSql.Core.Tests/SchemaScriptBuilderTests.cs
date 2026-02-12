using System.Collections.Generic;
using MehSql.Core.Schema;
using Xunit;

namespace MehSql.Core.Tests;

public sealed class SchemaScriptBuilderTests
{
    [Fact]
    public void BuildTableDdl_IncludesCreateTableAndIndexes()
    {
        var table = new TableNode("main", "users");
        table.Columns.Add(new ColumnNode("id", "INTEGER", isNullable: false, isPrimaryKey: true));
        table.Columns.Add(new ColumnNode("name", "TEXT", isNullable: false));
        table.Indexes.Add(new IndexNode("idx_users_name", false, new List<string> { "name" }));

        var ddl = SchemaScriptBuilder.BuildTableDdl(table);

        Assert.Contains("CREATE TABLE", ddl);
        Assert.Contains("\"users\"", ddl);
        Assert.Contains("CREATE INDEX", ddl);
        Assert.Contains("\"idx_users_name\"", ddl);
    }

    [Fact]
    public void BuildCrudTemplates_UsesPostgresStyleParameters()
    {
        var table = new TableNode("main", "users");
        table.Columns.Add(new ColumnNode("id", "INTEGER", isNullable: false, isPrimaryKey: true));
        table.Columns.Add(new ColumnNode("name", "TEXT", isNullable: false));

        var sql = SchemaScriptBuilder.BuildCrudTemplates(table);

        Assert.Contains("INSERT INTO", sql);
        Assert.Contains("$1", sql);
        Assert.Contains("$2", sql);
    }
}
