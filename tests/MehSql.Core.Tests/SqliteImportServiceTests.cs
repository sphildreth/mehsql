using MehSql.Core.Import;
using Xunit;

namespace MehSql.Core.Tests;

public sealed class SqliteImportServiceTests
{
    #region Type Mapping

    [Theory]
    [InlineData("INTEGER", "INT64")]
    [InlineData("INT", "INT64")]
    [InlineData("BIGINT", "INT64")]
    [InlineData("SMALLINT", "INT64")]
    [InlineData("TINYINT", "INT64")]
    [InlineData("REAL", "FLOAT64")]
    [InlineData("FLOAT", "FLOAT64")]
    [InlineData("DOUBLE", "FLOAT64")]
    [InlineData("DOUBLE PRECISION", "FLOAT64")]
    [InlineData("TEXT", "TEXT")]
    [InlineData("VARCHAR(255)", "TEXT")]
    [InlineData("CHAR(10)", "TEXT")]
    [InlineData("CLOB", "TEXT")]
    [InlineData("BLOB", "BLOB")]
    [InlineData("BOOLEAN", "BOOL")]
    [InlineData("BOOL", "BOOL")]
    [InlineData("UUID", "UUID")]
    [InlineData("DECIMAL(10,2)", "DECIMAL(10,2)")]
    [InlineData("NUMERIC(18,4)", "DECIMAL(18,4)")]
    [InlineData("DECIMAL", "DECIMAL(18,6)")]
    [InlineData("NUMERIC", "DECIMAL(18,6)")]
    [InlineData("", "TEXT")]
    [InlineData("WHATEVER", "TEXT")]
    public void MapDeclaredTypeToDecentDb_MapsCorrectly(string input, string expected)
    {
        var result = SqliteImportService.MapDeclaredTypeToDecentDb(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region Identifier Normalization

    [Fact]
    public void NormalizeIdentifier_Lower_ConvertsToLowercase()
    {
        Assert.Equal("my_table", SqliteImportService.NormalizeIdentifier("My_Table", "lower"));
    }

    [Fact]
    public void NormalizeIdentifier_Preserve_KeepsOriginal()
    {
        Assert.Equal("My_Table", SqliteImportService.NormalizeIdentifier("My_Table", "preserve"));
    }

    [Fact]
    public void BuildNameMaps_DetectsTableCollision()
    {
        var tables = new List<SqliteTable>
        {
            new("Users", [new("id", "INT", false, true)], [], [], []),
            new("users", [new("id", "INT", false, true)], [], [], [])
        };

        // Same name after lowercasing — but same source name maps to same dest, so no collision
        // Actually "Users" and "users" both map to "users" but from different sources — collision!
        var ex = Assert.Throws<ConversionException>(() =>
            SqliteImportService.BuildNameMaps(tables, "lower"));

        Assert.Contains("collision", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNameMaps_DetectsColumnCollision()
    {
        var tables = new List<SqliteTable>
        {
            new("test", [
                new("Name", "TEXT", false, false),
                new("name", "TEXT", false, false)
            ], [], [], [])
        };

        var ex = Assert.Throws<ConversionException>(() =>
            SqliteImportService.BuildNameMaps(tables, "lower"));

        Assert.Contains("collision", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildNameMaps_NoCollisionWithPreserve()
    {
        var tables = new List<SqliteTable>
        {
            new("test", [
                new("Name", "TEXT", false, false),
                new("name", "TEXT", false, false)
            ], [], [], [])
        };

        var (tableMap, colMap) = SqliteImportService.BuildNameMaps(tables, "preserve");
        Assert.Equal("Name", colMap["test"]["Name"]);
        Assert.Equal("name", colMap["test"]["name"]);
    }

    #endregion

    #region Validation

    [Fact]
    public void ValidateSupported_RejectsCompositePrimaryKey()
    {
        var table = new SqliteTable("test", [
            new("a", "INT", false, true),
            new("b", "INT", false, true)
        ], [], [], []);

        var ex = Assert.Throws<ConversionException>(() =>
            SqliteImportService.ValidateSupported(table));

        Assert.Contains("Composite primary key", ex.Message);
    }

    [Fact]
    public void ValidateSupported_AcceptsSinglePrimaryKey()
    {
        var table = new SqliteTable("test", [
            new("id", "INT", false, true),
            new("name", "TEXT", false, false)
        ], [], [], []);

        // Should not throw
        SqliteImportService.ValidateSupported(table);
    }

    [Fact]
    public void ValidateSupported_RejectsMultiForeignKeyFromSameColumn()
    {
        var table = new SqliteTable("test", [
            new("id", "INT", false, true),
            new("ref", "INT", false, false)
        ], [
            new("ref", "tableA", "id"),
            new("ref", "tableB", "id")
        ], [], []);

        var ex = Assert.Throws<ConversionException>(() =>
            SqliteImportService.ValidateSupported(table));

        Assert.Contains("Multiple foreign key", ex.Message);
    }

    #endregion

    #region Topological Sort

    [Fact]
    public void ToposortTables_SimpleOrdering()
    {
        var parent = new SqliteTable("parent", [new("id", "INT", false, true)], [], [], []);
        var child = new SqliteTable("child", [
            new("id", "INT", false, true),
            new("parent_id", "INT", false, false)
        ], [new("parent_id", "parent", "id")], [], []);

        var result = SqliteImportService.ToposortTables([parent, child]);

        Assert.Equal("parent", result[0].Name);
        Assert.Equal("child", result[1].Name);
    }

    [Fact]
    public void ToposortTables_ReverseInputOrder()
    {
        var parent = new SqliteTable("parent", [new("id", "INT", false, true)], [], [], []);
        var child = new SqliteTable("child", [
            new("id", "INT", false, true),
            new("parent_id", "INT", false, false)
        ], [new("parent_id", "parent", "id")], [], []);

        // Input in wrong order
        var result = SqliteImportService.ToposortTables([child, parent]);

        Assert.Equal("parent", result[0].Name);
        Assert.Equal("child", result[1].Name);
    }

    [Fact]
    public void ToposortTables_DetectsCycle()
    {
        var a = new SqliteTable("a", [
            new("id", "INT", false, true),
            new("b_id", "INT", false, false)
        ], [new("b_id", "b", "id")], [], []);

        var b = new SqliteTable("b", [
            new("id", "INT", false, true),
            new("a_id", "INT", false, false)
        ], [new("a_id", "a", "id")], [], []);

        var ex = Assert.Throws<ConversionException>(() =>
            SqliteImportService.ToposortTables([a, b]));

        Assert.Contains("cycle", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToposortTables_DetectsMissingTable()
    {
        var child = new SqliteTable("child", [
            new("id", "INT", false, true),
            new("parent_id", "INT", false, false)
        ], [new("parent_id", "missing_table", "id")], [], []);

        var ex = Assert.Throws<ConversionException>(() =>
            SqliteImportService.ToposortTables([child]));

        Assert.Contains("missing table", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToposortTables_DiamondDependency()
    {
        var root = new SqliteTable("root", [new("id", "INT", false, true)], [], [], []);
        var left = new SqliteTable("left", [
            new("id", "INT", false, true),
            new("root_id", "INT", false, false)
        ], [new("root_id", "root", "id")], [], []);
        var right = new SqliteTable("right", [
            new("id", "INT", false, true),
            new("root_id", "INT", false, false)
        ], [new("root_id", "root", "id")], [], []);
        var leaf = new SqliteTable("leaf", [
            new("id", "INT", false, true),
            new("left_id", "INT", false, false),
            new("right_id", "INT", false, false)
        ], [
            new("left_id", "left", "id"),
            new("right_id", "right", "id")
        ], [], []);

        var result = SqliteImportService.ToposortTables([leaf, right, left, root]);

        // Root must come first, leaf must come last
        Assert.Equal("root", result[0].Name);
        Assert.Equal("leaf", result[3].Name);
    }

    [Fact]
    public void ToposortTables_SelfReferencing_DoesNotCycle()
    {
        var table = new SqliteTable("categories", [
            new("id", "INT", false, true),
            new("parent_id", "INT", false, false)
        ], [new("parent_id", "categories", "id")], [], []);

        var result = SqliteImportService.ToposortTables([table]);
        Assert.Single(result);
        Assert.Equal("categories", result[0].Name);
    }

    #endregion

    #region Row Adaptation

    [Fact]
    public void AdaptValue_BoolColumn_ConvertsSqliteIntToBool()
    {
        var col = new SqliteColumn("active", "BOOLEAN", false, false);

        Assert.Equal(true, SqliteImportService.AdaptValue(col, 1L));
        Assert.Equal(false, SqliteImportService.AdaptValue(col, 0L));
    }

    [Fact]
    public void AdaptValue_BoolColumn_PreservesBool()
    {
        var col = new SqliteColumn("active", "BOOLEAN", false, false);
        Assert.Equal(true, SqliteImportService.AdaptValue(col, true));
    }

    [Fact]
    public void AdaptValue_NonBoolColumn_PassesThrough()
    {
        var col = new SqliteColumn("name", "TEXT", false, false);
        Assert.Equal("hello", SqliteImportService.AdaptValue(col, "hello"));
    }

    [Fact]
    public void AdaptValue_DbNull_ReturnsDbNull()
    {
        var col = new SqliteColumn("name", "TEXT", false, false);
        Assert.Equal(DBNull.Value, SqliteImportService.AdaptValue(col, DBNull.Value));
    }

    #endregion

    #region Quote Identifier

    [Fact]
    public void QuoteIdentifier_QuotesSimpleName()
    {
        Assert.Equal("\"my_table\"", SqliteImportService.QuoteIdentifier("my_table"));
    }

    [Fact]
    public void QuoteIdentifier_EscapesEmbeddedQuotes()
    {
        Assert.Equal("\"my\"\"table\"", SqliteImportService.QuoteIdentifier("my\"table"));
    }

    #endregion
}
