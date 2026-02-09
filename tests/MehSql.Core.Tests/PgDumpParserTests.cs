using MehSql.Core.Import;
using Xunit;

namespace MehSql.Core.Tests;

public sealed class PgDumpParserTests
{
    #region MapPgTypeToDecentDb

    [Theory]
    [InlineData("integer", "INT64")]
    [InlineData("bigint", "INT64")]
    [InlineData("smallint", "INT64")]
    [InlineData("serial", "INT64")]
    [InlineData("int4", "INT64")]
    [InlineData("int8", "INT64")]
    [InlineData("int2", "INT64")]
    [InlineData("bigserial", "INT64")]
    [InlineData("smallserial", "INT64")]
    [InlineData("boolean", "BOOL")]
    [InlineData("bool", "BOOL")]
    [InlineData("real", "FLOAT64")]
    [InlineData("float4", "FLOAT64")]
    [InlineData("double precision", "FLOAT64")]
    [InlineData("float8", "FLOAT64")]
    [InlineData("numeric(10,2)", "DECIMAL(10,2)")]
    [InlineData("numeric", "TEXT")]
    [InlineData("decimal(18,4)", "DECIMAL(18,4)")]
    [InlineData("character varying(255)", "TEXT")]
    [InlineData("varchar(100)", "TEXT")]
    [InlineData("text", "TEXT")]
    [InlineData("char(1)", "TEXT")]
    [InlineData("character(10)", "TEXT")]
    [InlineData("bpchar", "TEXT")]
    [InlineData("uuid", "UUID")]
    [InlineData("bytea", "BLOB")]
    [InlineData("timestamp with time zone", "TEXT")]
    [InlineData("timestamp without time zone", "TEXT")]
    [InlineData("timestamptz", "TEXT")]
    [InlineData("date", "TEXT")]
    [InlineData("timestamp", "TEXT")]
    [InlineData("json", "TEXT")]
    [InlineData("jsonb", "TEXT")]
    [InlineData("text[]", "TEXT")]
    [InlineData("integer[]", "TEXT")]
    [InlineData("somethingcustom", "TEXT")]
    public void MapPgTypeToDecentDb_MapsCorrectly(string input, string expected)
    {
        var result = PgDumpParser.MapPgTypeToDecentDb(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region NormalizeIdentifier

    [Theory]
    [InlineData("public.users", "users")]
    [InlineData("\"public\".\"Users\"", "Users")]
    [InlineData("\"MyTable\"", "MyTable")]
    [InlineData("plain", "plain")]
    [InlineData("  public.spaced  ", "spaced")]
    public void NormalizeIdentifier_StripsSchemaAndQuotes(string input, string expected)
    {
        var result = PgDumpParser.NormalizeIdentifier(input);
        Assert.Equal(expected, result);
    }

    #endregion

    #region ParseColumnList

    [Fact]
    public void ParseColumnList_QuotedColumns_ReturnsTrimmedNames()
    {
        var result = PgDumpParser.ParseColumnList("\"id\", \"name\"");
        Assert.Equal(["id", "name"], result);
    }

    [Fact]
    public void ParseColumnList_UnquotedColumns_ReturnsTrimmedNames()
    {
        var result = PgDumpParser.ParseColumnList("id, name");
        Assert.Equal(["id", "name"], result);
    }

    [Fact]
    public void ParseColumnList_SingleColumn_ReturnsSingleElement()
    {
        var result = PgDumpParser.ParseColumnList("\"email\"");
        Assert.Single(result);
        Assert.Equal("email", result[0]);
    }

    #endregion

    #region ParseColumnLine

    [Fact]
    public void ParseColumnLine_IntegerNotNull_ParsesCorrectly()
    {
        var col = PgDumpParser.ParseColumnLine("\"id\" integer NOT NULL,");

        Assert.NotNull(col);
        Assert.Equal("id", col.Name);
        Assert.Equal("integer", col.PgType);
        Assert.Equal("INT64", col.DecentDbType);
        Assert.True(col.NotNull);
    }

    [Fact]
    public void ParseColumnLine_CharacterVarying_ParsesCorrectly()
    {
        var col = PgDumpParser.ParseColumnLine("\"name\" character varying(255),");

        Assert.NotNull(col);
        Assert.Equal("name", col.Name);
        Assert.Equal("character varying(255)", col.PgType);
        Assert.Equal("TEXT", col.DecentDbType);
        Assert.False(col.NotNull);
    }

    [Fact]
    public void ParseColumnLine_BooleanWithDefault_ParsesCorrectly()
    {
        var col = PgDumpParser.ParseColumnLine("\"active\" boolean DEFAULT true");

        Assert.NotNull(col);
        Assert.Equal("active", col.Name);
        Assert.Equal("boolean", col.PgType);
        Assert.Equal("BOOL", col.DecentDbType);
        Assert.False(col.NotNull);
    }

    [Fact]
    public void ParseColumnLine_NumericWithPrecision_ParsesCorrectly()
    {
        var col = PgDumpParser.ParseColumnLine("\"amount\" numeric(10,2) NOT NULL");

        Assert.NotNull(col);
        Assert.Equal("amount", col.Name);
        Assert.Equal("numeric(10,2)", col.PgType);
        Assert.Equal("DECIMAL(10,2)", col.DecentDbType);
        Assert.True(col.NotNull);
    }

    [Fact]
    public void ParseColumnLine_TimestampWithoutTimeZone_ParsesCorrectly()
    {
        // ExtractPgType recognizes "WITHOUT TIME ZONE" after "timestamp", then scans
        // the continuation "time zone" — "time" isn't a recognized continuation keyword
        // on its own, so the parser captures "timestamp without time zone" up to "time"
        // boundary. The DecentDbType still maps to TEXT regardless.
        var col = PgDumpParser.ParseColumnLine("\"created_at\" timestamp without time zone");

        Assert.NotNull(col);
        Assert.Equal("created_at", col.Name);
        Assert.Equal("TEXT", col.DecentDbType);
        Assert.False(col.NotNull);
    }

    [Fact]
    public void ParseColumnLine_UnquotedName_ParsesCorrectly()
    {
        var col = PgDumpParser.ParseColumnLine("score real NOT NULL,");

        Assert.NotNull(col);
        Assert.Equal("score", col.Name);
        Assert.Equal("real", col.PgType);
        Assert.Equal("FLOAT64", col.DecentDbType);
        Assert.True(col.NotNull);
    }

    [Fact]
    public void ParseColumnLine_EmptyLine_ReturnsNull()
    {
        Assert.Null(PgDumpParser.ParseColumnLine(""));
        Assert.Null(PgDumpParser.ParseColumnLine("   "));
    }

    #endregion

    #region ParseSchema — CREATE TABLE

    [Fact]
    public void ParseSchema_SimpleTable_ExtractsTableAndColumns()
    {
        const string dump = """
            CREATE TABLE public.users (
                "id" integer NOT NULL,
                "name" character varying(255)
            );
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        Assert.Single(schema.Tables);
        var table = schema.Tables[0];
        Assert.Equal("users", table.Name);
        Assert.Equal(2, table.Columns.Count);

        Assert.Equal("id", table.Columns[0].Name);
        Assert.Equal("INT64", table.Columns[0].DecentDbType);
        Assert.True(table.Columns[0].NotNull);

        Assert.Equal("name", table.Columns[1].Name);
        Assert.Equal("TEXT", table.Columns[1].DecentDbType);
        Assert.False(table.Columns[1].NotNull);
    }

    [Fact]
    public void ParseSchema_TableWithVariousTypes_MapsAllCorrectly()
    {
        const string dump = """
            CREATE TABLE public.products (
                "id" bigint NOT NULL,
                "price" numeric(10,2),
                "active" boolean DEFAULT false,
                "data" jsonb,
                "photo" bytea,
                "uid" uuid
            );
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        var table = schema.Tables[0];
        Assert.Equal("products", table.Name);
        Assert.Equal(6, table.Columns.Count);

        Assert.Equal("INT64", table.Columns[0].DecentDbType);
        Assert.Equal("DECIMAL(10,2)", table.Columns[1].DecentDbType);
        Assert.Equal("BOOL", table.Columns[2].DecentDbType);
        Assert.Equal("TEXT", table.Columns[3].DecentDbType);
        Assert.Equal("BLOB", table.Columns[4].DecentDbType);
        Assert.Equal("UUID", table.Columns[5].DecentDbType);
    }

    [Fact]
    public void ParseSchema_QuotedTableName_ExtractsCorrectly()
    {
        const string dump = """
            CREATE TABLE "public"."MyTable" (
                "col1" text
            );
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        Assert.Single(schema.Tables);
        Assert.Equal("MyTable", schema.Tables[0].Name);
    }

    #endregion

    #region ParseSchema — ALTER TABLE PRIMARY KEY

    [Fact]
    public void ParseSchema_PrimaryKey_SetsIsPrimaryKeyAndNotNull()
    {
        const string dump = """
            CREATE TABLE public.users (
                "id" integer,
                "name" text
            );
            ALTER TABLE ONLY public.users ADD CONSTRAINT users_pkey PRIMARY KEY (id);
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        var table = schema.Tables[0];
        Assert.True(table.Columns[0].IsPrimaryKey);
        Assert.True(table.Columns[0].NotNull);
        Assert.False(table.Columns[1].IsPrimaryKey);
    }

    [Fact]
    public void ParseSchema_CompositePrimaryKey_SetsBothColumns()
    {
        const string dump = """
            CREATE TABLE public.order_items (
                "order_id" integer,
                "item_id" integer,
                "qty" integer
            );
            ALTER TABLE ONLY public.order_items ADD CONSTRAINT order_items_pkey PRIMARY KEY (order_id, item_id);
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        var table = schema.Tables[0];
        Assert.True(table.Columns[0].IsPrimaryKey);
        Assert.True(table.Columns[1].IsPrimaryKey);
        Assert.False(table.Columns[2].IsPrimaryKey);
    }

    #endregion

    #region ParseSchema — ALTER TABLE FOREIGN KEY

    [Fact]
    public void ParseSchema_ForeignKey_AddsFkToTable()
    {
        const string dump = """
            CREATE TABLE public.users (
                "id" integer NOT NULL
            );
            CREATE TABLE public.orders (
                "id" integer NOT NULL,
                "user_id" integer NOT NULL
            );
            ALTER TABLE ONLY public.orders ADD CONSTRAINT orders_user_fk FOREIGN KEY (user_id) REFERENCES public.users(id);
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        var orders = schema.Tables.First(t => t.Name == "orders");
        Assert.Single(orders.ForeignKeys);

        var fk = orders.ForeignKeys[0];
        Assert.Equal("orders_user_fk", fk.ConstraintName);
        Assert.Equal("user_id", fk.FromColumn);
        Assert.Equal("users", fk.ToTable);
        Assert.Equal("id", fk.ToColumn);
    }

    #endregion

    #region ParseSchema — CREATE INDEX

    [Fact]
    public void ParseSchema_NonUniqueIndex_ParsesCorrectly()
    {
        const string dump = """
            CREATE TABLE public.users (
                "id" integer NOT NULL,
                "email" text
            );
            CREATE INDEX idx_users_email ON public.users USING btree (email);
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        Assert.Single(schema.Indexes);
        var idx = schema.Indexes[0];
        Assert.Equal("idx_users_email", idx.Name);
        Assert.Equal("users", idx.Table);
        Assert.Equal(["email"], idx.Columns);
        Assert.False(idx.IsUnique);
    }

    [Fact]
    public void ParseSchema_UniqueIndex_ParsesCorrectly()
    {
        const string dump = """
            CREATE TABLE public.users (
                "id" integer NOT NULL,
                "email" text
            );
            CREATE UNIQUE INDEX idx_users_email_uniq ON public.users USING btree (email);
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        Assert.Single(schema.Indexes);
        var idx = schema.Indexes[0];
        Assert.Equal("idx_users_email_uniq", idx.Name);
        Assert.True(idx.IsUnique);
    }

    [Fact]
    public void ParseSchema_MultiColumnIndex_ParsesAllColumns()
    {
        const string dump = """
            CREATE TABLE public.events (
                "id" integer NOT NULL,
                "user_id" integer,
                "created_at" timestamp without time zone
            );
            CREATE INDEX idx_events_user_created ON public.events USING btree (user_id, created_at);
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        Assert.Single(schema.Indexes);
        Assert.Equal(["user_id", "created_at"], schema.Indexes[0].Columns);
    }

    #endregion

    #region ParseSchema — COPY Block Row Counting

    [Fact]
    public void ParseSchema_CopyBlock_CountsRows()
    {
        const string dump = """
            CREATE TABLE public.users (
                "id" integer NOT NULL,
                "name" text
            );
            COPY public.users (id, name) FROM stdin;
            1	Alice
            2	Bob
            3	Charlie
            \.
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        Assert.True(schema.RowCounts.ContainsKey("users"));
        Assert.Equal(3, schema.RowCounts["users"]);
    }

    [Fact]
    public void ParseSchema_EmptyCopyBlock_CountsZeroRows()
    {
        const string dump = """
            CREATE TABLE public.empty_table (
                "id" integer NOT NULL
            );
            COPY public.empty_table (id) FROM stdin;
            \.
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        Assert.True(schema.RowCounts.ContainsKey("empty_table"));
        Assert.Equal(0, schema.RowCounts["empty_table"]);
    }

    #endregion

    #region ParseSchema — Comments and Blank Lines

    [Fact]
    public void ParseSchema_CommentsAndBlankLines_AreSkipped()
    {
        const string dump = """
            -- This is a comment
            
            -- Another comment
            CREATE TABLE public.items (
                "id" integer NOT NULL
            );
            
            -- End of dump
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        Assert.Single(schema.Tables);
        Assert.Equal("items", schema.Tables[0].Name);
    }

    [Fact]
    public void ParseSchema_EmptyInput_ReturnsEmptySchema()
    {
        using var reader = new StringReader("");
        var schema = PgDumpParser.ParseSchema(reader);

        Assert.Empty(schema.Tables);
        Assert.Empty(schema.Indexes);
        Assert.Empty(schema.RowCounts);
    }

    #endregion

    #region ParseSchema — Full Integration

    [Fact]
    public void ParseSchema_FullDump_ParsesAllElements()
    {
        const string dump = """
            --
            -- PostgreSQL database dump
            --
            
            CREATE TABLE public.users (
                "id" integer NOT NULL,
                "email" character varying(255) NOT NULL,
                "active" boolean DEFAULT true
            );
            
            CREATE TABLE public.orders (
                "id" integer NOT NULL,
                "user_id" integer NOT NULL,
                "total" numeric(10,2)
            );
            
            ALTER TABLE ONLY public.users ADD CONSTRAINT users_pkey PRIMARY KEY (id);
            ALTER TABLE ONLY public.orders ADD CONSTRAINT orders_pkey PRIMARY KEY (id);
            ALTER TABLE ONLY public.orders ADD CONSTRAINT orders_user_fk FOREIGN KEY (user_id) REFERENCES public.users(id);
            
            CREATE UNIQUE INDEX idx_users_email ON public.users USING btree (email);
            CREATE INDEX idx_orders_user ON public.orders USING btree (user_id);
            
            COPY public.users (id, email, active) FROM stdin;
            1	alice@test.com	t
            2	bob@test.com	t
            \.
            
            COPY public.orders (id, user_id, total) FROM stdin;
            10	1	99.99
            \.
            """;

        using var reader = new StringReader(dump);
        var schema = PgDumpParser.ParseSchema(reader);

        Assert.Equal(2, schema.Tables.Count);

        var users = schema.Tables.First(t => t.Name == "users");
        Assert.Equal(3, users.Columns.Count);
        Assert.True(users.Columns[0].IsPrimaryKey);
        Assert.True(users.Columns[1].NotNull);
        Assert.Empty(users.ForeignKeys);

        var orders = schema.Tables.First(t => t.Name == "orders");
        Assert.Equal(3, orders.Columns.Count);
        Assert.True(orders.Columns[0].IsPrimaryKey);
        Assert.Single(orders.ForeignKeys);
        Assert.Equal("users", orders.ForeignKeys[0].ToTable);

        Assert.Equal(2, schema.Indexes.Count);
        Assert.True(schema.Indexes.First(i => i.Name == "idx_users_email").IsUnique);
        Assert.False(schema.Indexes.First(i => i.Name == "idx_orders_user").IsUnique);

        Assert.Equal(2, schema.RowCounts["users"]);
        Assert.Equal(1, schema.RowCounts["orders"]);
    }

    #endregion
}
