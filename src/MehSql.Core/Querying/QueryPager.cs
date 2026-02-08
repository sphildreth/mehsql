using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MehSql.Core.Connections;
using MehSql.Core.Execution;

namespace MehSql.Core.Querying;

/// <summary>
/// Default implementation of IQueryPager using QueryExecutor.
/// </summary>
public sealed class QueryPager : IQueryPager
{
    private readonly IQueryExecutor _executor;

    public QueryPager(IConnectionFactory connectionFactory)
    {
        _executor = new QueryExecutor(connectionFactory);
    }

    public async Task<QueryPage> ExecuteFirstPageAsync(string sql, QueryOptions options, CancellationToken ct)
    {
        var result = await _executor.ExecutePageAsync(sql, options, offset: null, ct);

        var nextToken = result.Rows.Count >= options.PageSize
            ? new QueryPageToken($"offset:{options.PageSize}")
            : null;

        return new QueryPage(
            Columns: result.Columns,
            Rows: result.Rows.Select(r => r.Values).ToList(),
            NextToken: nextToken,
            Timings: result.Timings
        );
    }

    public async Task<QueryPage> ExecuteNextPageAsync(string sql, QueryOptions options, QueryPageToken token, CancellationToken ct)
    {
        if (!TryParseOffset(token.Value, out var offset))
        {
            throw new ArgumentException("Invalid page token format.", nameof(token));
        }

        var result = await _executor.ExecutePageAsync(sql, options, offset, ct);

        var nextToken = result.Rows.Count >= options.PageSize
            ? new QueryPageToken($"offset:{offset + options.PageSize}")
            : null;

        return new QueryPage(
            Columns: result.Columns,
            Rows: result.Rows.Select(r => r.Values).ToList(),
            NextToken: nextToken,
            Timings: result.Timings
        );
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
