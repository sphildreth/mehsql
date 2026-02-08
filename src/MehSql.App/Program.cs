using System;
using Avalonia;
using Avalonia.ReactiveUI;
using MehSql.App.Logging;
using MehSql.App.Services;
using Serilog;

namespace MehSql.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Configure Serilog
        Log.Logger = LoggingConfiguration.CreateLogger();
        
        try
        {
            Log.Information("Starting MehSQL application");
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Application terminated unexpectedly");
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
