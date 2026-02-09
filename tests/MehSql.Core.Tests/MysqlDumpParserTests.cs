using MehSql.Core.Import;
using Xunit;

namespace MehSql.Core.Tests;

public sealed class MysqlDumpParserTests
{
    #region MapMysqlTypeToDecentDb

    [Theory]
    [InlineData("int(11)", "INT64")]
    [InlineData("bigint(20)", "INT64")]
    [InlineData("smallint", "INT64")]
    [InlineData("mediumint", "INT64")]
    [InlineData("tinyint(1)", "BOOL")]
    [InlineData("tinyint(4)", "INT64")]
    [InlineData("float", "FLOAT64")]
    [InlineData("double", "FLOAT64")]
    [InlineData("real", "FLOAT64")]
    [InlineData("decimal(10,2)", "DECIMAL(10,2)")]
    [InlineData("decimal", "DECIMAL(18,6)")]
    [InlineData("varchar(255)", "TEXT")]
    [InlineData("char(2)", "TEXT")]
    [InlineData("text", "TEXT")]
    [InlineData("mediumtext", "TEXT")]
    [InlineData("longtext", "TEXT")]
    [InlineData("date", "TEXT")]
    [InlineData("datetime", "TEXT")]
    [InlineData("timestamp", "TEXT")]
    [InlineData("time", "TEXT")]
    [InlineData("year", "TEXT")]
    [InlineData("blob", "BLOB")]
    [InlineData("mediumblob", "BLOB")]
    [InlineData("longblob", "BLOB")]
    [InlineData("binary(16)", "BLOB")]
    [InlineData("varbinary(255)", "BLOB")]
    [InlineData("enum('a','b')", "TEXT")]
    [InlineData("set('x','y')", "TEXT")]
    [InlineData("json", "TEXT")]
    [InlineData("int unsigned", "INT64")]
    [InlineData("unknown_type", "TEXT")]
    public void MapMysqlTypeToDecentDb_MapsCorrectly(string input, string expected)
    {
        var result = MysqlDumpParser.MapMysqlTypeToDecentDb(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region StripBackticks

    [Fact]
    public void StripBackticks_RemovesBackticks()
    {
        Assert.Equal("users", MysqlDumpParser.StripBackticks("`users`"));
    }

    [Fact]
    public void StripBackticks_PlainIdentifier_Unchanged()
    {
        Assert.Equal("plain", MysqlDumpParser.StripBackticks("plain"));
    }

    [Fact]
    public void StripBackticks_SingleBacktick_Unchanged()
    {
        Assert.Equal("`", MysqlDumpParser.StripBackticks("`"));
    }

    #endregion

    #region ParseSchema — CREATE TABLE

    [Fact]
    public void ParseSchema_SimpleTable_ParsesColumnsCorrectly()
    {
        var dump = """
            CREATE TABLE `users` (
              `id` int(11) NOT NULL AUTO_INCREMENT,
              `name` varchar(255) NOT NULL,
              `birthday` date DEFAULT NULL,
              PRIMARY KEY (`id`)
            ) ENGINE=InnoDB;
            """;

        using var reader = new StringReader(dump);
        var schema = MysqlDumpParser.ParseSchema(reader);

        Assert.Single(schema.Tables);
        var table = schema.Tables[0];
        Assert.Equal("users", table.Name);
        Assert.Equal(3, table.Columns.Count);

        var id = table.Columns[0];
        Assert.Equal("id", id.Name);
        Assert.Equal("INT64", id.DecentDbType);
        Assert.True(id.NotNull);
        Assert.True(id.IsPrimaryKey);
        Assert.True(id.IsAutoIncrement);

        var name = table.Columns[1];
        Assert.Equal("name", name.Name);
        Assert.Equal("TEXT", name.DecentDbType);
        Assert.True(name.NotNull);
        Assert.False(name.IsPrimaryKey);

        var birthday = table.Columns[2];
        Assert.Equal("birthday", birthday.Name);
        Assert.Equal("TEXT", birthday.DecentDbType);
        Assert.False(birthday.NotNull);
    }

    [Fact]
    public void ParseSchema_CompositePrimaryKey_MarksBothColumns()
    {
        var dump = """
            CREATE TABLE `order_items` (
              `order_id` int(11) NOT NULL,
              `product_id` int(11) NOT NULL,
              `qty` int(11) DEFAULT NULL,
              PRIMARY KEY (`order_id`, `product_id`)
            ) ENGINE=InnoDB;
            """;

        using var reader = new StringReader(dump);
        var schema = MysqlDumpParser.ParseSchema(reader);

        var table = schema.Tables[0];
        Assert.True(table.Columns[0].IsPrimaryKey);
        Assert.True(table.Columns[1].IsPrimaryKey);
        Assert.False(table.Columns[2].IsPrimaryKey);
    }

    #endregion

    #region ParseSchema — UNIQUE KEY and KEY

    [Fact]
    public void ParseSchema_UniqueKey_CreatesUniqueIndex()
    {
        var dump = """
            CREATE TABLE `users` (
              `id` int(11) NOT NULL AUTO_INCREMENT,
              `email` varchar(255) NOT NULL,
              PRIMARY KEY (`id`),
              UNIQUE KEY `idx_email` (`email`)
            ) ENGINE=InnoDB;
            """;

        using var reader = new StringReader(dump);
        var schema = MysqlDumpParser.ParseSchema(reader);

        var table = schema.Tables[0];
        Assert.Single(table.Indexes);
        Assert.True(table.Indexes[0].IsUnique);
        Assert.Equal("idx_email", table.Indexes[0].Name);

        var emailCol = table.Columns.First(c => c.Name == "email");
        Assert.True(emailCol.IsUnique);
    }

    [Fact]
    public void ParseSchema_NonUniqueKey_CreatesNonUniqueIndex()
    {
        var dump = """
            CREATE TABLE `orders` (
              `id` int(11) NOT NULL AUTO_INCREMENT,
              `user_id` int(11) NOT NULL,
              PRIMARY KEY (`id`),
              KEY `idx_user` (`user_id`)
            ) ENGINE=InnoDB;
            """;

        using var reader = new StringReader(dump);
        var schema = MysqlDumpParser.ParseSchema(reader);

        var table = schema.Tables[0];
        Assert.Single(table.Indexes);
        Assert.False(table.Indexes[0].IsUnique);
        Assert.Equal("idx_user", table.Indexes[0].Name);
    }

    #endregion

    #region ParseSchema — CONSTRAINT FOREIGN KEY

    [Fact]
    public void ParseSchema_ForeignKey_ParsedCorrectly()
    {
        var dump = """
            CREATE TABLE `orders` (
              `id` int(11) NOT NULL AUTO_INCREMENT,
              `user_id` int(11) NOT NULL,
              PRIMARY KEY (`id`),
              CONSTRAINT `fk_user` FOREIGN KEY (`user_id`) REFERENCES `users` (`id`)
            ) ENGINE=InnoDB;
            """;

        using var reader = new StringReader(dump);
        var schema = MysqlDumpParser.ParseSchema(reader);

        var table = schema.Tables[0];
        Assert.Single(table.ForeignKeys);
        var fk = table.ForeignKeys[0];
        Assert.Equal("fk_user", fk.ConstraintName);
        Assert.Equal("user_id", fk.FromColumn);
        Assert.Equal("users", fk.ToTable);
        Assert.Equal("id", fk.ToColumn);
    }

    #endregion

    #region ParseSchema — INSERT row counting

    [Fact]
    public void ParseSchema_InsertValues_CountsRows()
    {
        var dump = """
            CREATE TABLE `t` (
              `id` int(11) NOT NULL,
              `val` varchar(100) DEFAULT NULL,
              PRIMARY KEY (`id`)
            ) ENGINE=InnoDB;
            INSERT INTO `t` VALUES (1,'a'),(2,'b');
            """;

        using var reader = new StringReader(dump);
        var schema = MysqlDumpParser.ParseSchema(reader);

        Assert.Equal(2, schema.RowCounts["t"]);
    }

    [Fact]
    public void ParseSchema_MultipleInserts_Accumulate()
    {
        var dump = """
            CREATE TABLE `t` (
              `id` int(11) NOT NULL,
              PRIMARY KEY (`id`)
            ) ENGINE=InnoDB;
            INSERT INTO `t` VALUES (1),(2);
            INSERT INTO `t` VALUES (3);
            """;

        using var reader = new StringReader(dump);
        var schema = MysqlDumpParser.ParseSchema(reader);

        Assert.Equal(3, schema.RowCounts["t"]);
    }

    #endregion

    #region ParseSchema — skips irrelevant lines

    [Fact]
    public void ParseSchema_SkipsIrrelevantLines()
    {
        var dump = """
            -- MySQL dump
            /*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
            SET @saved_cs_client     = @@character_set_client;
            USE `mydb`;
            DROP TABLE IF EXISTS `t`;
            LOCK TABLES `t` WRITE;
            UNLOCK TABLES;

            CREATE TABLE `t` (
              `id` int(11) NOT NULL,
              PRIMARY KEY (`id`)
            ) ENGINE=InnoDB;

            CREATE DATABASE IF NOT EXISTS `mydb`;
            """;

        using var reader = new StringReader(dump);
        var schema = MysqlDumpParser.ParseSchema(reader);

        Assert.Single(schema.Tables);
        Assert.Equal("t", schema.Tables[0].Name);
    }

    #endregion

    #region ParseInsertValues

    private static IReadOnlyList<MysqlColumn> MakeColumns(params (string Name, string DecentDbType)[] cols)
    {
        return cols.Select(c => new MysqlColumn(c.Name, "", c.DecentDbType, false)).ToList();
    }

    [Fact]
    public void ParseInsertValues_Integers()
    {
        var columns = MakeColumns(("a", "INT64"), ("b", "INT64"), ("c", "INT64"));
        var rows = MysqlDumpParser.ParseInsertValues("(1,2,3)", columns).ToList();

        Assert.Single(rows);
        Assert.Equal(new object[] { 1L, 2L, 3L }, rows[0]);
    }

    [Fact]
    public void ParseInsertValues_Strings()
    {
        var columns = MakeColumns(("a", "TEXT"), ("b", "TEXT"));
        var rows = MysqlDumpParser.ParseInsertValues("('hello','world')", columns).ToList();

        Assert.Single(rows);
        Assert.Equal("hello", rows[0][0]);
        Assert.Equal("world", rows[0][1]);
    }

    [Fact]
    public void ParseInsertValues_Null()
    {
        var columns = MakeColumns(("a", "TEXT"));
        var rows = MysqlDumpParser.ParseInsertValues("(NULL)", columns).ToList();

        Assert.Single(rows);
        Assert.Equal(DBNull.Value, rows[0][0]);
    }

    [Fact]
    public void ParseInsertValues_MixedTypes()
    {
        var columns = MakeColumns(("a", "INT64"), ("b", "TEXT"), ("c", "TEXT"), ("d", "FLOAT64"));
        var rows = MysqlDumpParser.ParseInsertValues("(1,'text',NULL,3.14)", columns).ToList();

        Assert.Single(rows);
        Assert.Equal(1L, rows[0][0]);
        Assert.Equal("text", rows[0][1]);
        Assert.Equal(DBNull.Value, rows[0][2]);
        Assert.Equal(3.14, rows[0][3]);
    }

    [Fact]
    public void ParseInsertValues_BackslashEscapedQuote()
    {
        var columns = MakeColumns(("a", "TEXT"));
        var rows = MysqlDumpParser.ParseInsertValues(@"('it\'s')", columns).ToList();

        Assert.Single(rows);
        Assert.Equal("it's", rows[0][0]);
    }

    [Fact]
    public void ParseInsertValues_BackslashEscapedBackslash()
    {
        var columns = MakeColumns(("a", "TEXT"));
        var rows = MysqlDumpParser.ParseInsertValues(@"('path\\to\\file')", columns).ToList();

        Assert.Single(rows);
        Assert.Equal(@"path\to\file", rows[0][0]);
    }

    [Fact]
    public void ParseInsertValues_BackslashEscapedNewline()
    {
        var columns = MakeColumns(("a", "TEXT"));
        var rows = MysqlDumpParser.ParseInsertValues(@"('line1\nline2')", columns).ToList();

        Assert.Single(rows);
        Assert.Equal("line1\nline2", rows[0][0]);
    }

    [Fact]
    public void ParseInsertValues_DoubleQuotedSingleQuote()
    {
        var columns = MakeColumns(("a", "TEXT"));
        var rows = MysqlDumpParser.ParseInsertValues("('it''s')", columns).ToList();

        Assert.Single(rows);
        Assert.Equal("it's", rows[0][0]);
    }

    [Fact]
    public void ParseInsertValues_BooleanColumn()
    {
        var columns = MakeColumns(("a", "BOOL"));
        var rows = MysqlDumpParser.ParseInsertValues("(1)", columns).ToList();

        Assert.Single(rows);
        Assert.Equal(true, rows[0][0]);
    }

    [Fact]
    public void ParseInsertValues_BooleanColumnFalse()
    {
        var columns = MakeColumns(("a", "BOOL"));
        var rows = MysqlDumpParser.ParseInsertValues("(0)", columns).ToList();

        Assert.Single(rows);
        Assert.Equal(false, rows[0][0]);
    }

    [Fact]
    public void ParseInsertValues_MultipleValueGroups()
    {
        var columns = MakeColumns(("a", "INT64"), ("b", "TEXT"));
        var rows = MysqlDumpParser.ParseInsertValues("(1,'a'),(2,'b')", columns).ToList();

        Assert.Equal(2, rows.Count);
        Assert.Equal(1L, rows[0][0]);
        Assert.Equal("a", rows[0][1]);
        Assert.Equal(2L, rows[1][0]);
        Assert.Equal("b", rows[1][1]);
    }

    #endregion

    #region ParseSchema — full dump

    [Fact]
    public void ParseSchema_FullDump_ParsesCompletely()
    {
        var dump = """
            -- MySQL dump 10.13  Distrib 8.0.33
            /*!40101 SET @OLD_CHARACTER_SET_CLIENT=@@CHARACTER_SET_CLIENT */;
            SET NAMES utf8mb4;

            DROP TABLE IF EXISTS `countries`;
            CREATE TABLE `countries` (
              `id` int(11) NOT NULL AUTO_INCREMENT,
              `code` char(2) NOT NULL,
              `name` varchar(100) NOT NULL,
              `population` bigint(20) DEFAULT NULL,
              `area` decimal(10,2) DEFAULT NULL,
              `active` tinyint(1) NOT NULL DEFAULT '1',
              PRIMARY KEY (`id`),
              UNIQUE KEY `idx_code` (`code`),
              KEY `idx_name` (`name`),
              CONSTRAINT `fk_region` FOREIGN KEY (`id`) REFERENCES `regions` (`id`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

            LOCK TABLES `countries` WRITE;
            INSERT INTO `countries` VALUES (1,'US','United States',331000000,9833520.00,1),(2,'CA','Canada',38000000,9984670.00,1);
            INSERT INTO `countries` VALUES (3,'MX','Mexico',128000000,1972550.00,1);
            UNLOCK TABLES;
            """;

        using var reader = new StringReader(dump);
        var schema = MysqlDumpParser.ParseSchema(reader);

        Assert.Single(schema.Tables);
        var table = schema.Tables[0];
        Assert.Equal("countries", table.Name);
        Assert.Equal(6, table.Columns.Count);

        // Verify column types
        Assert.Equal("INT64", table.Columns[0].DecentDbType);   // int(11)
        Assert.Equal("TEXT", table.Columns[1].DecentDbType);     // char(2)
        Assert.Equal("TEXT", table.Columns[2].DecentDbType);     // varchar(100)
        Assert.Equal("INT64", table.Columns[3].DecentDbType);    // bigint(20)
        Assert.Equal("DECIMAL(10,2)", table.Columns[4].DecentDbType); // decimal(10,2)
        Assert.Equal("BOOL", table.Columns[5].DecentDbType);     // tinyint(1)

        // PK
        Assert.True(table.Columns[0].IsPrimaryKey);
        Assert.True(table.Columns[0].IsAutoIncrement);

        // Indexes
        Assert.Equal(2, table.Indexes.Count);
        Assert.Contains(table.Indexes, i => i.Name == "idx_code" && i.IsUnique);
        Assert.Contains(table.Indexes, i => i.Name == "idx_name" && !i.IsUnique);

        // Unique flag on column
        Assert.True(table.Columns[1].IsUnique);

        // FK
        Assert.Single(table.ForeignKeys);
        Assert.Equal("fk_region", table.ForeignKeys[0].ConstraintName);

        // Row counts
        Assert.Equal(3, schema.RowCounts["countries"]);
    }

    #endregion

    #region BuildCreateTableSql

    [Fact]
    public void BuildCreateTableSql_SinglePk_NotNull_Unique()
    {
        var table = new MysqlTable("users", [
            new MysqlColumn("id", "int(11)", "INT64", true, IsPrimaryKey: true, IsAutoIncrement: true),
            new MysqlColumn("email", "varchar(255)", "TEXT", true, IsUnique: true),
            new MysqlColumn("name", "varchar(100)", "TEXT", false)
        ], [], []);

        var sql = MysqlDumpImportSource.BuildCreateTableSql(table, lowercaseIdentifiers: false);

        Assert.Equal(
            "CREATE TABLE \"users\" (\"id\" INT64 PRIMARY KEY, \"email\" TEXT UNIQUE NOT NULL, \"name\" TEXT)",
            sql);
    }

    [Fact]
    public void BuildCreateTableSql_CompositePk()
    {
        var table = new MysqlTable("order_items", [
            new MysqlColumn("order_id", "int(11)", "INT64", true, IsPrimaryKey: true),
            new MysqlColumn("product_id", "int(11)", "INT64", true, IsPrimaryKey: true),
            new MysqlColumn("qty", "int(11)", "INT64", false)
        ], [], []);

        var sql = MysqlDumpImportSource.BuildCreateTableSql(table, lowercaseIdentifiers: false);

        Assert.Equal(
            "CREATE TABLE \"order_items\" (\"order_id\" INT64 NOT NULL, \"product_id\" INT64 NOT NULL, \"qty\" INT64, PRIMARY KEY (\"order_id\", \"product_id\"))",
            sql);
    }

    [Fact]
    public void BuildCreateTableSql_LowercaseIdentifiers()
    {
        var table = new MysqlTable("MyTable", [
            new MysqlColumn("Id", "int(11)", "INT64", true, IsPrimaryKey: true)
        ], [], []);

        var sql = MysqlDumpImportSource.BuildCreateTableSql(table, lowercaseIdentifiers: true);

        Assert.Equal(
            "CREATE TABLE \"mytable\" (\"id\" INT64 PRIMARY KEY)",
            sql);
    }

    #endregion
}
