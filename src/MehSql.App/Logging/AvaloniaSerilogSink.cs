using Avalonia.Logging;
using Serilog;

namespace MehSql.App.Logging;

/// <summary>
/// Forwards Avalonia log messages to Serilog for unified diagnostics.
/// </summary>
internal sealed class AvaloniaSerilogSink : ILogSink
{
    public bool IsEnabled(LogEventLevel level, string area) =>
        level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
    {
        Serilog.Log.Logger.Warning("[Avalonia:{Area}] {Message} (source: {Source})", area, messageTemplate, source?.GetType().Name);
    }

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        var message = string.Format(messageTemplate.Replace("{", "{{").Replace("}", "}}"), propertyValues);
        Serilog.Log.Logger.Warning("[Avalonia:{Area}] {Message} (source: {Source})", area, message, source?.GetType().Name);
    }
}
