namespace MehSql.Core.Querying;

public sealed record QueryOptions(
    int PageSize = 500,
    bool PreferKeysetPaging = true,
    int MaxCachedPages = 5,
    bool ApplyDefaultLimit = true
);
