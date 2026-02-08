# ADR-0004: Page Caching Implementation

## Status
Accepted

## Context
Phase 3 requires implementing page caching for virtualized results. When users scroll through large result sets, we want to avoid re-fetching pages they've already seen. This improves perceived performance and reduces load on DecentDB.

## Decision

### LRU Cache with Configurable Size
- Implemented `CachedQueryPager` as a decorator around `IQueryPager`
- Default cache size: 5 pages (configurable via `QueryOptions.MaxCachedPages`)
- LRU (Least Recently Used) eviction policy when cache is full

### Cache Key Strategy
- Cache key is `SQL_HASH:OFFSET` to differentiate between different queries and pages
- Uses SQL string hash code combined with offset for uniqueness

### Cache Statistics
- Track hits, misses, and hit ratio for monitoring
- Exposed via `GetStatistics()` method
- Clear cache via `ClearCache()` method

### Thread Safety
- All cache operations protected by `lock` for thread safety
- Minimal lock scope to reduce contention

## Implementation Details

### New Types
- `CachedQueryPager`: Decorator implementing caching logic
- `PageCacheEntry`: Entry metadata (SQL, offset, page, last accessed, access count)
- `CacheStatistics`: Immutable statistics record

### Changes to Existing Types
- `QueryOptions`: Added `MaxCachedPages` parameter (default: 5)

## Alternatives Considered

1. **No Caching**: Rejected - poor UX when scrolling back through results
2. **Infinite Cache**: Rejected - memory pressure with large datasets
3. **Time-based Expiration**: Rejected - adds complexity, LRU is simpler and effective
4. **WeakReference Cache**: Rejected - unpredictable eviction, LRU is more deterministic

## Consequences

### Positive
- Reduced database load when users scroll back through results
- Better perceived performance
- Configurable cache size per query
- Observable statistics for monitoring

### Negative
- Additional memory usage (N pages cached)
- Cache invalidation not implemented (acceptable for read-only query tool)
- Non-deterministic ordering warning still required (handled separately)

## Related
- ADR-0003: Results Paging + Virtualization Strategy
- Phase 3: Virtualized Results + Paging + Cache
