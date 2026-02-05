namespace MehSql.Core.Querying;

public sealed record QueryTimings(TimeSpan? DbExecutionTime, TimeSpan FetchTime, TimeSpan? UiBindTime);
