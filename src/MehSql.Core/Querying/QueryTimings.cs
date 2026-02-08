namespace MehSql.Core.Querying;

public sealed record QueryTimings(TimeSpan? DbExecutionTime, TimeSpan FetchTime, TimeSpan? UiBindTime)
{
    /// <summary>
    /// Gets the total time including DB execution, fetch, and UI bind.
    /// </summary>
    public TimeSpan TotalTime
    {
        get
        {
            var total = TimeSpan.Zero;
            if (DbExecutionTime.HasValue) total += DbExecutionTime.Value;
            total += FetchTime;
            if (UiBindTime.HasValue) total += UiBindTime.Value;
            return total;
        }
    }
}
