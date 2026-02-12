using MehSql.Core.Querying;
using MehSql.Core.Schema;
using System.Threading.Tasks;
using Xunit;

namespace MehSql.Core.Tests;

public class AutocompleteTests
{
    private static SchemaRootNode CreateTestSchema()
    {
        var schema = new SchemaRootNode("test");

        var artists = new TableNode("main", "Artists");
        artists.Columns.Add(new ColumnNode("ArtistId", "INTEGER", false, null, true));
        artists.Columns.Add(new ColumnNode("Name", "TEXT", true));
        schema.Tables.Add(artists);

        var albums = new TableNode("main", "Albums");
        albums.Columns.Add(new ColumnNode("AlbumId", "INTEGER", false, null, true));
        albums.Columns.Add(new ColumnNode("Title", "TEXT", true));
        albums.Columns.Add(new ColumnNode("ArtistId", "INTEGER", false));
        schema.Tables.Add(albums);

        return schema;
    }

    private static SchemaRootNode CreateLowercaseSchema()
    {
        var schema = new SchemaRootNode("test");

        var artist = new TableNode("public", "artist");
        artist.Columns.Add(new ColumnNode("artist_id", "INTEGER", false, null, true));
        artist.Columns.Add(new ColumnNode("name", "TEXT", true));
        schema.Tables.Add(artist);

        var album = new TableNode("public", "album");
        album.Columns.Add(new ColumnNode("album_id", "INTEGER", false, null, true));
        album.Columns.Add(new ColumnNode("title", "TEXT", true));
        album.Columns.Add(new ColumnNode("artist_id", "INTEGER", false));
        schema.Tables.Add(album);

        return schema;
    }

    #region SqlParser Tests

    [Fact]
    public void SqlParser_ExtractsTableAlias()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM Artists a WHERE a.Name = 'Test'";

        var aliases = parser.ExtractTableAliases(sql);

        Assert.Single(aliases);
        Assert.Equal("Artists", aliases["a"]);
    }

    [Fact]
    public void SqlParser_ExtractsMultipleAliases()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM Artists a JOIN Albums al ON a.ArtistId = al.ArtistId";

        var aliases = parser.ExtractTableAliases(sql);

        Assert.Equal(2, aliases.Count);
        Assert.Equal("Artists", aliases["a"]);
        Assert.Equal("Albums", aliases["al"]);
    }

    [Fact]
    public void SqlParser_ExtractsAliasWithAsKeyword()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM Artists AS a";

        var aliases = parser.ExtractTableAliases(sql);

        Assert.Single(aliases);
        Assert.Equal("Artists", aliases["a"]);
    }

    [Fact]
    public void SqlParser_IgnoresKeywordsAsAliases()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM Artists WHERE Name = 'Test'";

        var aliases = parser.ExtractTableAliases(sql);

        Assert.Empty(aliases);
    }

    [Fact]
    public void SqlParser_ExtractsQuotedTableAlias()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM \"artist\" a WHERE a.Name = 'Test'";

        var aliases = parser.ExtractTableAliases(sql);

        Assert.Single(aliases);
        Assert.Equal("artist", aliases["a"]);
    }

    [Fact]
    public void SqlParser_DetectsAliasDotContext_QuotedTable()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM \"artist\" a WHERE a.";

        var context = parser.GetContextAtPosition(sql, sql.Length);

        Assert.Equal("a", context.CurrentAlias);
        Assert.Equal("artist", context.Aliases["a"]);
    }

    [Fact]
    public void SqlParser_ExtractsQuotedJoinTableAlias()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM \"artist\" a JOIN \"album\" al ON a.ArtistId = al.ArtistId";

        var aliases = parser.ExtractTableAliases(sql);

        Assert.Equal(2, aliases.Count);
        Assert.Equal("artist", aliases["a"]);
        Assert.Equal("album", aliases["al"]);
    }

    [Fact]
    public void SqlParser_DetectsAliasDotContext()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM Artists a WHERE a.";

        var context = parser.GetContextAtPosition(sql, sql.Length);

        Assert.Equal("a", context.CurrentAlias);
        Assert.Equal("Artists", context.Aliases["a"]);
    }

    [Fact]
    public void SqlParser_DetectsAliasDotPartialContext()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM Artists a WHERE a.Na";

        var context = parser.GetContextAtPosition(sql, sql.Length);

        Assert.Equal("a", context.CurrentAlias);
        Assert.Equal("Na", context.PartialWord);
    }

    [Fact]
    public void SqlParser_DetectsFromClause()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM ";

        var context = parser.GetContextAtPosition(sql, sql.Length);

        Assert.Equal(SqlClauseType.From, context.CurrentClause);
    }

    [Fact]
    public void SqlParser_DetectsSelectClause()
    {
        var parser = new SqlParser();
        var sql = "SELECT ";

        var context = parser.GetContextAtPosition(sql, sql.Length);

        Assert.Equal(SqlClauseType.Select, context.CurrentClause);
    }

    [Fact]
    public void SqlParser_DetectsWhereClause()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM Artists WHERE ";

        var context = parser.GetContextAtPosition(sql, sql.Length);

        Assert.Equal(SqlClauseType.Where, context.CurrentClause);
    }

    [Fact]
    public void SqlParser_ExtractsPartialWord()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM Art";

        var context = parser.GetContextAtPosition(sql, sql.Length);

        Assert.Equal("Art", context.PartialWord);
    }

    [Fact]
    public void SqlParser_ReturnsEmptyContextInsideString()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM Artists WHERE Name = 'Te";

        var context = parser.GetContextAtPosition(sql, sql.Length);

        // Inside a string literal — no aliases, no clause detection
        Assert.Empty(context.Aliases);
        Assert.Null(context.CurrentAlias);
    }

    [Fact]
    public void SqlParser_ReturnsEmptyContextInsideComment()
    {
        var parser = new SqlParser();
        var sql = "SELECT * FROM Artists -- some ";

        var context = parser.GetContextAtPosition(sql, sql.Length);

        Assert.Empty(context.Aliases);
    }

    #endregion

    #region AutocompleteCache Tests

    [Fact]
    public void AutocompleteCache_GetColumnsForTable_ReturnsColumns()
    {
        var cache = new AutocompleteCache(CreateTestSchema());

        var columns = cache.GetColumnsForTable("Artists");

        Assert.Equal(2, columns.Count);
        Assert.Equal("ArtistId", columns[0].Name);
        Assert.Equal("Name", columns[1].Name);
    }

    [Fact]
    public void AutocompleteCache_GetColumnsForTable_CaseInsensitive()
    {
        var cache = new AutocompleteCache(CreateTestSchema());

        var columns = cache.GetColumnsForTable("artists");

        Assert.Equal(2, columns.Count);
    }

    [Fact]
    public void AutocompleteCache_GetColumnsForTable_UnknownTable_ReturnsEmpty()
    {
        var cache = new AutocompleteCache(CreateTestSchema());

        var columns = cache.GetColumnsForTable("NonExistentTable");

        Assert.Empty(columns);
    }

    [Fact]
    public void AutocompleteCache_GetAllTables_ReturnsAllTables()
    {
        var cache = new AutocompleteCache(CreateTestSchema());

        var tables = cache.GetAllTables();

        Assert.Equal(2, tables.Count);
    }

    [Fact]
    public void AutocompleteCache_GetAllKeywords_ReturnsKeywords()
    {
        var cache = new AutocompleteCache(CreateTestSchema());

        var keywords = cache.GetAllKeywords();

        Assert.True(keywords.Count > 0);
    }

    #endregion

    #region AutocompleteService Tests

    [Fact]
    public async Task AutocompleteService_AliasDot_ReturnsColumns()
    {
        var parser = new SqlParser();
        var service = new AutocompleteService(parser);
        var cache = new AutocompleteCache(CreateTestSchema());
        var sql = "SELECT * FROM Artists a WHERE a.";

        var suggestions = await service.GetSuggestionsAsync(sql, sql.Length, cache);

        Assert.Equal(2, suggestions.Count);
        Assert.Contains(suggestions, s => s.DisplayText == "ArtistId");
        Assert.Contains(suggestions, s => s.DisplayText == "Name");
        Assert.All(suggestions, s => Assert.Equal(AutocompleteItemType.Column, s.Type));
    }

    [Fact]
    public async Task AutocompleteService_AliasDot_PrimaryKeyHigherPriority()
    {
        var parser = new SqlParser();
        var service = new AutocompleteService(parser);
        var cache = new AutocompleteCache(CreateTestSchema());
        var sql = "SELECT * FROM Artists a WHERE a.";

        var suggestions = await service.GetSuggestionsAsync(sql, sql.Length, cache);

        var pk = suggestions.First(s => s.DisplayText == "ArtistId");
        var other = suggestions.First(s => s.DisplayText == "Name");
        Assert.True(pk.Priority > other.Priority);
    }

    [Fact]
    public async Task AutocompleteService_FromClause_ReturnsTables()
    {
        var parser = new SqlParser();
        var service = new AutocompleteService(parser);
        var cache = new AutocompleteCache(CreateTestSchema());
        var sql = "SELECT * FROM ";

        var suggestions = await service.GetSuggestionsAsync(sql, sql.Length, cache);

        Assert.Equal(2, suggestions.Count);
        Assert.All(suggestions, s => Assert.Equal(AutocompleteItemType.Table, s.Type));
    }

    [Fact]
    public async Task AutocompleteService_FromClause_FiltersByPartialWord()
    {
        var parser = new SqlParser();
        var service = new AutocompleteService(parser);
        var cache = new AutocompleteCache(CreateTestSchema());
        var sql = "SELECT * FROM Art";

        var suggestions = await service.GetSuggestionsAsync(sql, sql.Length, cache);

        Assert.Single(suggestions);
        Assert.Equal("Artists", suggestions[0].DisplayText);
    }

    [Fact]
    public async Task AutocompleteService_Default_ReturnsKeywords()
    {
        var parser = new SqlParser();
        var service = new AutocompleteService(parser);
        var cache = new AutocompleteCache(CreateTestSchema());
        var sql = "SEL";

        var suggestions = await service.GetSuggestionsAsync(sql, sql.Length, cache);

        Assert.Single(suggestions);
        Assert.Equal("SELECT", suggestions[0].DisplayText);
        Assert.Equal(AutocompleteItemType.Keyword, suggestions[0].Type);
    }

    [Fact]
    public async Task AutocompleteService_ColumnDescription_IsDataType()
    {
        var parser = new SqlParser();
        var service = new AutocompleteService(parser);
        var cache = new AutocompleteCache(CreateTestSchema());
        var sql = "SELECT * FROM Artists a WHERE a.";

        var suggestions = await service.GetSuggestionsAsync(sql, sql.Length, cache);

        var nameCol = suggestions.First(s => s.DisplayText == "Name");
        Assert.Equal("TEXT", nameCol.Description);
    }

    [Fact]
    public async Task AutocompleteService_AliasDotPartial_FiltersColumns()
    {
        var parser = new SqlParser();
        var service = new AutocompleteService(parser);
        var cache = new AutocompleteCache(CreateTestSchema());
        var sql = "SELECT * FROM Artists a WHERE a.Na";

        var suggestions = await service.GetSuggestionsAsync(sql, sql.Length, cache);

        Assert.Single(suggestions);
        Assert.Equal("Name", suggestions[0].DisplayText);
        Assert.Equal(AutocompleteItemType.Column, suggestions[0].Type);
    }

    [Fact]
    public async Task AutocompleteService_QuotedTable_AliasDot_ReturnsColumns()
    {
        var parser = new SqlParser();
        var service = new AutocompleteService(parser);
        var cache = new AutocompleteCache(CreateLowercaseSchema());
        var sql = "SELECT * FROM \"artist\" a WHERE a.";

        var suggestions = await service.GetSuggestionsAsync(sql, sql.Length, cache);

        Assert.Equal(2, suggestions.Count);
        Assert.Contains(suggestions, s => s.DisplayText == "artist_id");
        Assert.Contains(suggestions, s => s.DisplayText == "name");
        Assert.All(suggestions, s => Assert.Equal(AutocompleteItemType.Column, s.Type));
    }

    [Fact]
    public async Task AutocompleteService_QuotedTable_LowercaseSql_ReturnsColumns()
    {
        var parser = new SqlParser();
        var service = new AutocompleteService(parser);
        var cache = new AutocompleteCache(CreateLowercaseSchema());
        var sql = "select * from \"artist\" a where a.";

        var suggestions = await service.GetSuggestionsAsync(sql, sql.Length, cache);

        Assert.Equal(2, suggestions.Count);
        Assert.Contains(suggestions, s => s.DisplayText == "artist_id");
        Assert.Contains(suggestions, s => s.DisplayText == "name");
        Assert.All(suggestions, s => Assert.Equal(AutocompleteItemType.Column, s.Type));
    }

    #endregion
}
