using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MehSql.App.Services;
using MehSql.App.ViewModels;
using MehSql.App.Views;
using MehSql.Core.Connections;
using Serilog;

namespace MehSql.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create a default database path in temp folder
            var dbPath = Path.Combine(Path.GetTempPath(), $"mehsql_{Guid.NewGuid()}.db");
            var connectionFactory = new ConnectionFactory(dbPath);

            var vm = new MainWindowViewModel(connectionFactory);
            desktop.MainWindow = new MainWindow
            {
                DataContext = vm
            };

            // Handle command-line arguments: open .ddb or load .sql
            var ddbArg = desktop.Args?.FirstOrDefault(a =>
                a.EndsWith(".ddb", StringComparison.OrdinalIgnoreCase) && File.Exists(a));
            var sqlArg = desktop.Args?.FirstOrDefault(a =>
                a.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && File.Exists(a));

            if (ddbArg is not null || sqlArg is not null)
            {
                // Defer until the window is loaded so the UI is ready
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    try
                    {
                        if (ddbArg is not null)
                        {
                            Log.Information("Opening database from command-line argument: {Path}", ddbArg);
                            await vm.OpenDatabaseAsync(Path.GetFullPath(ddbArg));
                        }

                        if (sqlArg is not null)
                        {
                            Log.Information("Loading SQL file from command-line argument: {Path}", sqlArg);
                            vm.SqlText = await File.ReadAllTextAsync(Path.GetFullPath(sqlArg));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error processing command-line arguments");
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
