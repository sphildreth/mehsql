using Serilog;
using Serilog.Core;

namespace MehSql.App.Logging
{
    public static class LoggingConfiguration
    {
        public static Logger CreateLogger()
        {
            return new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File("logs/mehsql-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .WriteTo.Console(outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
    }
}