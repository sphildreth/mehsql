using System.Threading;
using System.Threading.Tasks;

namespace MehSql.Core.Querying;

public interface IQueryPager
{
    Task<QueryPage> ExecuteFirstPageAsync(string sql, QueryOptions options, CancellationToken ct);
    Task<QueryPage> ExecuteNextPageAsync(string sql, QueryOptions options, QueryPageToken token, CancellationToken ct);
}
