using System.Text.Json;
using MehSql.Core.Import;
using Xunit;

namespace MehSql.Core.Tests;

public sealed class MysqlShellDumpParserTests
{
    private static IReadOnlyList<MysqlColumn> MakeColumns(params (string Name, string DecentDbType)[] cols)
    {
        return cols.Select(c => new MysqlColumn(c.Name, "", c.DecentDbType, false)).ToList();
    }

    #region ParseTsvLine — basic parsing

    [Fact]
    public void ParseTsvLine_TabSeparatedIntegers_ReturnsLongs()
    {
        var columns = MakeColumns(("a", "INT64"), ("b", "INT64"), ("c", "INT64"));
        var result = MysqlShellDumpParser.ParseTsvLine("1\t2\t3", columns);

        Assert.Equal(new object[] { 1L, 2L, 3L }, result);
    }

    [Fact]
    public void ParseTsvLine_StringValues_ReturnsStrings()
    {
        var columns = MakeColumns(("a", "TEXT"), ("b", "TEXT"));
        var result = MysqlShellDumpParser.ParseTsvLine("hello\tworld", columns);

        Assert.Equal(new object[] { "hello", "world" }, result);
    }

    [Fact]
    public void ParseTsvLine_BackslashN_ReturnsDbNull()
    {
        var columns = MakeColumns(("a", "TEXT"));
        var result = MysqlShellDumpParser.ParseTsvLine("\\N", columns);

        Assert.Single(result);
        Assert.Equal(DBNull.Value, result[0]);
    }

    [Fact]
    public void ParseTsvLine_MixedTypes_ReturnsCorrectTypes()
    {
        var columns = MakeColumns(("a", "INT64"), ("b", "TEXT"), ("c", "TEXT"), ("d", "FLOAT64"));
        var result = MysqlShellDumpParser.ParseTsvLine("1\thello\t\\N\t3.14", columns);

        Assert.Equal(1L, result[0]);
        Assert.Equal("hello", result[1]);
        Assert.Equal(DBNull.Value, result[2]);
        Assert.Equal(3.14, result[3]);
    }

    [Fact]
    public void ParseTsvLine_BooleanTrue_ReturnsTrue()
    {
        var columns = MakeColumns(("a", "BOOL"));
        var result = MysqlShellDumpParser.ParseTsvLine("1", columns);

        Assert.Single(result);
        Assert.Equal(true, result[0]);
    }

    [Fact]
    public void ParseTsvLine_BooleanFalse_ReturnsFalse()
    {
        var columns = MakeColumns(("a", "BOOL"));
        var result = MysqlShellDumpParser.ParseTsvLine("0", columns);

        Assert.Single(result);
        Assert.Equal(false, result[0]);
    }

    #endregion

    #region ParseTsvLine — escape sequences

    [Fact]
    public void ParseTsvLine_EscapedBackslashes_ReturnsLiteralBackslashes()
    {
        var columns = MakeColumns(("a", "TEXT"));
        var result = MysqlShellDumpParser.ParseTsvLine("path\\\\to\\\\file", columns);

        Assert.Single(result);
        Assert.Equal("path\\to\\file", result[0]);
    }

    [Fact]
    public void ParseTsvLine_EscapedNewline_ReturnsEmbeddedNewline()
    {
        var columns = MakeColumns(("a", "TEXT"));
        var result = MysqlShellDumpParser.ParseTsvLine("line1\\nline2", columns);

        Assert.Single(result);
        Assert.Equal("line1\nline2", result[0]);
    }

    [Fact]
    public void ParseTsvLine_EscapedTab_ReturnsEmbeddedTab()
    {
        var columns = MakeColumns(("a", "TEXT"));
        // The field itself contains "col1\tcol2" as escaped text (backslash + t).
        // But since SplitTsvFields splits on real tabs first, we need a single field.
        // A single field with escaped tab: "col1\\tcol2" in C# = col1\tcol2 in the TSV.
        var result = MysqlShellDumpParser.ParseTsvLine("col1\\tcol2", columns);

        Assert.Single(result);
        Assert.Equal("col1\tcol2", result[0]);
    }

    #endregion

    #region GetDataChunkFiles — file discovery

    [Fact]
    public void GetDataChunkFiles_ReturnsFilesInNumericOrder()
    {
        var tempDir = Directory.CreateTempSubdirectory("mehsql_test_chunks_");
        try
        {
            File.WriteAllText(Path.Combine(tempDir.FullName, "testdb@users@@0.tsv.zst"), "");
            File.WriteAllText(Path.Combine(tempDir.FullName, "testdb@users@@1.tsv.zst"), "");
            File.WriteAllText(Path.Combine(tempDir.FullName, "testdb@users@@10.tsv.zst"), "");

            var result = MysqlShellDumpParser.GetDataChunkFiles(tempDir.FullName, "testdb", "users");

            Assert.Equal(3, result.Count);
            Assert.EndsWith("testdb@users@@0.tsv.zst", Path.GetFileName(result[0]));
            Assert.EndsWith("testdb@users@@1.tsv.zst", Path.GetFileName(result[1]));
            Assert.EndsWith("testdb@users@@10.tsv.zst", Path.GetFileName(result[2]));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void GetDataChunkFiles_NoMatchingFiles_ReturnsEmpty()
    {
        var tempDir = Directory.CreateTempSubdirectory("mehsql_test_empty_");
        try
        {
            var result = MysqlShellDumpParser.GetDataChunkFiles(tempDir.FullName, "testdb", "users");

            Assert.Empty(result);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    #endregion

    #region ParseDumpDirectory — full directory parsing

    [Fact]
    public void ParseDumpDirectory_ValidDump_ParsesTablesCorrectly()
    {
        var tempDir = Directory.CreateTempSubdirectory("mehsql_test_dump_");
        try
        {
            // @.json with schema info
            var atJson = new { schemas = new[] { "testdb" } };
            File.WriteAllText(Path.Combine(tempDir.FullName, "@.json"), JsonSerializer.Serialize(atJson));

            // testdb@users.json with table metadata
            var tableMeta = new
            {
                options = new
                {
                    columns = new[] { "id", "name", "email" },
                    primaryIndex = "PRIMARY",
                    compression = "zstd",
                    fieldsTerminatedBy = "\t",
                    fieldsEscapedBy = "\\"
                },
                extension = "tsv.zst",
                chunking = true
            };
            File.WriteAllText(Path.Combine(tempDir.FullName, "testdb@users.json"), JsonSerializer.Serialize(tableMeta));

            // testdb@users.sql with CREATE TABLE DDL
            var createSql = """
                CREATE TABLE `users` (
                  `id` int(11) NOT NULL AUTO_INCREMENT,
                  `name` varchar(255) NOT NULL,
                  `email` varchar(255) DEFAULT NULL,
                  PRIMARY KEY (`id`)
                ) ENGINE=InnoDB;
                """;
            File.WriteAllText(Path.Combine(tempDir.FullName, "testdb@users.sql"), createSql);

            var schema = MysqlShellDumpParser.ParseDumpDirectory(tempDir.FullName);

            Assert.Equal("testdb", schema.DefaultSchema);
            Assert.Single(schema.Tables);
            Assert.Empty(schema.Warnings);

            var table = schema.Tables[0];
            Assert.Equal("users", table.Name);
            Assert.Equal(3, table.Columns.Count);
            Assert.Equal("id", table.Columns[0].Name);
            Assert.Equal("INT64", table.Columns[0].DecentDbType);
            Assert.True(table.Columns[0].IsPrimaryKey);

            // Verify table meta was stored
            Assert.True(schema.TableMetas.ContainsKey("testdb.users"));
            var meta = schema.TableMetas["testdb.users"];
            Assert.Equal(new[] { "id", "name", "email" }, meta.Columns);
            Assert.Equal("PRIMARY", meta.PrimaryIndex);
            Assert.True(meta.Chunking);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    #endregion

    #region ParseDumpDirectory — missing @.json

    [Fact]
    public void ParseDumpDirectory_MissingAtJson_ThrowsFileNotFound()
    {
        var tempDir = Directory.CreateTempSubdirectory("mehsql_test_noat_");
        try
        {
            Assert.Throws<FileNotFoundException>(() =>
                MysqlShellDumpParser.ParseDumpDirectory(tempDir.FullName));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    #endregion

    #region ParseDumpDirectory — multiple tables

    [Fact]
    public void ParseDumpDirectory_MultipleTables_ParsesBoth()
    {
        var tempDir = Directory.CreateTempSubdirectory("mehsql_test_multi_");
        try
        {
            var atJson = new { schemas = new[] { "testdb" } };
            File.WriteAllText(Path.Combine(tempDir.FullName, "@.json"), JsonSerializer.Serialize(atJson));

            // First table: users
            var usersMeta = new
            {
                options = new
                {
                    columns = new[] { "id", "name" },
                    primaryIndex = "PRIMARY"
                }
            };
            File.WriteAllText(Path.Combine(tempDir.FullName, "testdb@users.json"), JsonSerializer.Serialize(usersMeta));
            File.WriteAllText(Path.Combine(tempDir.FullName, "testdb@users.sql"), """
                CREATE TABLE `users` (
                  `id` int(11) NOT NULL AUTO_INCREMENT,
                  `name` varchar(255) NOT NULL,
                  PRIMARY KEY (`id`)
                ) ENGINE=InnoDB;
                """);

            // Second table: orders
            var ordersMeta = new
            {
                options = new
                {
                    columns = new[] { "id", "user_id", "total" },
                    primaryIndex = "PRIMARY"
                }
            };
            File.WriteAllText(Path.Combine(tempDir.FullName, "testdb@orders.json"), JsonSerializer.Serialize(ordersMeta));
            File.WriteAllText(Path.Combine(tempDir.FullName, "testdb@orders.sql"), """
                CREATE TABLE `orders` (
                  `id` int(11) NOT NULL AUTO_INCREMENT,
                  `user_id` int(11) NOT NULL,
                  `total` decimal(10,2) DEFAULT NULL,
                  PRIMARY KEY (`id`)
                ) ENGINE=InnoDB;
                """);

            var schema = MysqlShellDumpParser.ParseDumpDirectory(tempDir.FullName);

            Assert.Equal(2, schema.Tables.Count);
            Assert.Empty(schema.Warnings);

            var tableNames = schema.Tables.Select(t => t.Name).OrderBy(n => n).ToList();
            Assert.Equal(new[] { "orders", "users" }, tableNames);

            Assert.True(schema.TableMetas.ContainsKey("testdb.users"));
            Assert.True(schema.TableMetas.ContainsKey("testdb.orders"));
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    #endregion
}
