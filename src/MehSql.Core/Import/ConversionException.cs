namespace MehSql.Core.Import;

/// <summary>
/// Thrown when the SQLite import encounters an unrecoverable structural issue.
/// </summary>
public sealed class ConversionException : Exception
{
    public ConversionException(string message) : base(message) { }
    public ConversionException(string message, Exception inner) : base(message, inner) { }
}
