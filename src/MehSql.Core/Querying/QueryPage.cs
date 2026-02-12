using System.Collections.Generic;

namespace MehSql.Core.Querying;

public sealed record QueryPage(
    IReadOnlyList<ColumnInfo> Columns,
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows,
    QueryPageToken? NextToken,
    QueryTimings Timings,
    bool DefaultLimitApplied = false,
    int? AppliedDefaultLimit = null
);
