using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MehSql.Core.Querying;
using Xunit;

namespace MehSql.Core.Tests;

public class CachedQueryPagerTests
{
    [Fact]
    public async Task ExecuteFirstPageAsync_CachesResult()
    {
        var inner = new FakeQueryPager();
        var cached = new CachedQueryPager(inner, maxCacheSize: 3);

        // First call should hit the inner pager
        var page1 = await cached.ExecuteFirstPageAsync("SELECT 1", new QueryOptions(), default);
        Assert.Equal(1, inner.FirstPageCallCount);

        // Second call with same SQL should use cache
        var page2 = await cached.ExecuteFirstPageAsync("SELECT 1", new QueryOptions(), default);
        Assert.Equal(1, inner.FirstPageCallCount); // Should not increment
        Assert.Same(page1.Rows, page2.Rows);
    }

    [Fact]
    public async Task ExecuteNextPageAsync_CachesResult()
    {
        var inner = new FakeQueryPager();
        var cached = new CachedQueryPager(inner, maxCacheSize: 3);

        // Get first page
        var firstPage = await cached.ExecuteFirstPageAsync("SELECT 1", new QueryOptions(), default);

        // First next page call should hit the inner pager
        var page1 = await cached.ExecuteNextPageAsync("SELECT 1", new QueryOptions(), firstPage.NextToken!, default);
        Assert.Equal(1, inner.NextPageCallCount);

        // Second call with same token should use cache
        var page2 = await cached.ExecuteNextPageAsync("SELECT 1", new QueryOptions(), firstPage.NextToken!, default);
        Assert.Equal(1, inner.NextPageCallCount); // Should not increment
    }

    [Fact]
    public async Task Cache_EvictsLRU_WhenFull()
    {
        var inner = new FakeQueryPager();
        var cached = new CachedQueryPager(inner, maxCacheSize: 2);

        // Fill cache with 2 pages
        var page1 = await cached.ExecuteFirstPageAsync("SELECT 1", new QueryOptions(), default);
        var page2 = await cached.ExecuteNextPageAsync("SELECT 1", new QueryOptions(), page1.NextToken!, default);

        // Access first page again to update LRU
        await cached.ExecuteFirstPageAsync("SELECT 1", new QueryOptions(), default);

        // Add third page - should evict page2 (least recently used)
        var page3 = await cached.ExecuteNextPageAsync("SELECT 1", new QueryOptions(), page2.NextToken!, default);

        // Page1 should still be cached, page2 evicted
        var stats = cached.GetStatistics();
        Assert.Equal(2, stats.TotalEntries);

        // Accessing page2 again should hit inner pager
        await cached.ExecuteNextPageAsync("SELECT 1", new QueryOptions(), page1.NextToken!, default);
        Assert.True(inner.NextPageCallCount > 1);
    }

    [Fact]
    public async Task Cache_TracksHitsAndMisses()
    {
        var inner = new FakeQueryPager();
        var cached = new CachedQueryPager(inner, maxCacheSize: 3);

        // First call - miss
        await cached.ExecuteFirstPageAsync("SELECT 1", new QueryOptions(), default);
        var stats1 = cached.GetStatistics();
        Assert.Equal(0, stats1.Hits);
        Assert.Equal(1, stats1.Misses);

        // Second call - hit
        await cached.ExecuteFirstPageAsync("SELECT 1", new QueryOptions(), default);
        var stats2 = cached.GetStatistics();
        Assert.Equal(1, stats2.Hits);
        Assert.Equal(1, stats2.Misses);
        Assert.Equal(0.5, stats2.HitRatio);
    }

    [Fact]
    public void ClearCache_RemovesAllEntries()
    {
        var inner = new FakeQueryPager();
        var cached = new CachedQueryPager(inner, maxCacheSize: 3);

        cached.ClearCache();
        var stats = cached.GetStatistics();

        Assert.Equal(0, stats.TotalEntries);
        Assert.Equal(0, stats.Hits);
        Assert.Equal(0, stats.Misses);
    }

    [Fact]
    public async Task DifferentSql_DifferentCacheKeys()
    {
        var inner = new FakeQueryPager();
        var cached = new CachedQueryPager(inner, maxCacheSize: 3);

        // Different SQL should have different cache entries
        await cached.ExecuteFirstPageAsync("SELECT 1", new QueryOptions(), default);
        await cached.ExecuteFirstPageAsync("SELECT 2", new QueryOptions(), default);

        var stats = cached.GetStatistics();
        Assert.Equal(2, stats.TotalEntries);
        Assert.Equal(2, inner.FirstPageCallCount); // Both should hit inner
    }

    [Fact]
    public async Task ExecuteNextPageAsync_InvalidToken_ThrowsArgumentException()
    {
        var inner = new FakeQueryPager();
        var cached = new CachedQueryPager(inner, maxCacheSize: 3);

        var invalidToken = new QueryPageToken("invalid");

        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await cached.ExecuteNextPageAsync("SELECT 1", new QueryOptions(), invalidToken, default);
        });
    }

    private sealed class FakeQueryPager : IQueryPager
    {
        public int FirstPageCallCount { get; private set; }
        public int NextPageCallCount { get; private set; }

        public Task<QueryPage> ExecuteFirstPageAsync(string sql, QueryOptions options, CancellationToken ct)
        {
            FirstPageCallCount++;
            var columns = new List<ColumnInfo> { new("id", "integer") };
            var rows = new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["id"] = FirstPageCallCount }
            };
            var token = new QueryPageToken($"offset:{options.PageSize}");
            var timings = new QueryTimings(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), null);

            return Task.FromResult(new QueryPage(columns, rows, token, timings));
        }

        public Task<QueryPage> ExecuteNextPageAsync(string sql, QueryOptions options, QueryPageToken token, CancellationToken ct)
        {
            NextPageCallCount++;
            var columns = new List<ColumnInfo> { new("id", "integer") };
            var rows = new List<IReadOnlyDictionary<string, object?>>
            {
                new Dictionary<string, object?> { ["id"] = NextPageCallCount }
            };

            // Parse offset from token
            var offsetStr = token.Value.Replace("offset:", "");
            var offset = int.Parse(offsetStr);
            var nextToken = new QueryPageToken($"offset:{offset + options.PageSize}");
            var timings = new QueryTimings(TimeSpan.FromMilliseconds(1), TimeSpan.FromMilliseconds(1), null);

            return Task.FromResult(new QueryPage(columns, rows, nextToken, timings));
        }
    }
}
