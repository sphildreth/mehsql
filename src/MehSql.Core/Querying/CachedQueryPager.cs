using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MehSql.Core.Querying;

/// <summary>
/// Decorator that adds LRU page caching to an IQueryPager implementation.
/// </summary>
public sealed class CachedQueryPager : IQueryPager
{
    private readonly IQueryPager _inner;
    private readonly int _maxCacheSize;
    private readonly Dictionary<string, PageCacheEntry> _cache;
    private readonly object _lock = new();
    private int _hits;
    private int _misses;

    public CachedQueryPager(IQueryPager inner, int maxCacheSize = 5)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _maxCacheSize = maxCacheSize > 0 ? maxCacheSize : throw new ArgumentException("Cache size must be positive.", nameof(maxCacheSize));
        _cache = new Dictionary<string, PageCacheEntry>();
    }

    public async Task<QueryPage> ExecuteFirstPageAsync(string sql, QueryOptions options, CancellationToken ct)
    {
        var cacheKey = BuildCacheKey(sql, 0);

        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                entry.AccessCount++;
                _hits++;
                return entry.Page;
            }
        }

        _misses++;
        var page = await _inner.ExecuteFirstPageAsync(sql, options, ct);
        AddToCache(cacheKey, sql, 0, page);
        return page;
    }

    public async Task<QueryPage> ExecuteNextPageAsync(string sql, QueryOptions options, QueryPageToken token, CancellationToken ct)
    {
        if (!TryParseOffset(token.Value, out var offset))
        {
            throw new ArgumentException("Invalid page token format.", nameof(token));
        }

        var cacheKey = BuildCacheKey(sql, offset);

        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var entry))
            {
                entry.LastAccessed = DateTime.UtcNow;
                entry.AccessCount++;
                _hits++;
                return entry.Page;
            }
        }

        _misses++;
        var page = await _inner.ExecuteNextPageAsync(sql, options, token, ct);
        AddToCache(cacheKey, sql, offset, page);
        return page;
    }

    /// <summary>
    /// Gets current cache statistics.
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        lock (_lock)
        {
            var total = _hits + _misses;
            var ratio = total > 0 ? (double)_hits / total : 0;
            return new CacheStatistics(_cache.Count, _hits, _misses, ratio);
        }
    }

    /// <summary>
    /// Clears all cached pages.
    /// </summary>
    public void ClearCache()
    {
        lock (_lock)
        {
            _cache.Clear();
            _hits = 0;
            _misses = 0;
        }
    }

    private void AddToCache(string key, string sql, int offset, QueryPage page)
    {
        lock (_lock)
        {
            if (_cache.Count >= _maxCacheSize)
            {
                EvictLRU();
            }

            _cache[key] = new PageCacheEntry(sql, offset, page);
        }
    }

    private void EvictLRU()
    {
        if (_cache.Count == 0) return;

        var oldest = _cache.Values.OrderBy(e => e.LastAccessed).First();
        _cache.Remove(BuildCacheKey(oldest.Sql, oldest.Offset));
    }

    private static string BuildCacheKey(string sql, int offset)
    {
        return $"{offset}:{sql.Length}:{sql}";
    }

    private static bool TryParseOffset(string token, out int offset)
    {
        offset = 0;
        if (!token.StartsWith("offset:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var value = token.Substring(7);
        return int.TryParse(value, out offset);
    }
}
