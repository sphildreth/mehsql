using System;
using System.Collections.Generic;

namespace MehSql.Core.Querying;

/// <summary>
/// Cache entry for a query page with metadata for eviction policy.
/// </summary>
public sealed class PageCacheEntry
{
    public string Sql { get; }
    public int Offset { get; }
    public QueryPage Page { get; }
    public DateTime LastAccessed { get; set; }
    public int AccessCount { get; set; }

    public PageCacheEntry(string sql, int offset, QueryPage page)
    {
        Sql = sql;
        Offset = offset;
        Page = page;
        LastAccessed = DateTime.UtcNow;
        AccessCount = 1;
    }
}

/// <summary>
/// Cache statistics for monitoring cache performance.
/// </summary>
public sealed record CacheStatistics(
    int TotalEntries,
    int Hits,
    int Misses,
    double HitRatio
);
