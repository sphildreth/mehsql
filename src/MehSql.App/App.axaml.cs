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

            // Handle command-line arguments
            string? databaseFileToOpen = null;
            string? sqlFileToLoad = null;

            if (desktop.Args?.Length > 0)
            {
                foreach (var arg in desktop.Args)
                {
                    if (string.IsNullOrWhiteSpace(arg))
                    {
                        continue;
                    }

                    var fullPath = Path.GetFullPath(arg);
                    
                    if (File.Exists(fullPath))
                    {
                        // Check file type by extension
                        if (arg.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                        {
                            sqlFileToLoad = fullPath;
                            Log.Information("SQL file argument detected: {Path}", fullPath);
                        }
                        else if (arg.EndsWith(".ddb", StringComparison.OrdinalIgnoreCase) || 
                                 databaseFileToOpen == null)
                        {
                            // Accept .ddb files or any file as potential database if no database set yet
                            databaseFileToOpen = fullPath;
                            Log.Information("Database file argument detected: {Path}", fullPath);
                        }
                    }
                    else
                    {
                        Log.Warning("Command-line argument file not found: {Path}", arg);
                    }
                }
            }

            // Load schema and handle any command-line arguments after window opens
            desktop.MainWindow.Opened += async (_, _) =>
            {
                try
                {
                    if (databaseFileToOpen is not null)
                    {
                        Log.Information("Opening database from command-line: {Path}", databaseFileToOpen);
                        await vm.OpenDatabaseAsync(databaseFileToOpen);
                    }
                    else if (!string.IsNullOrEmpty(vm.SettingsService.Settings.LastDatabasePath) &&
                             File.Exists(vm.SettingsService.Settings.LastDatabasePath))
                    {
                        Log.Information("Reopening last database: {Path}", vm.SettingsService.Settings.LastDatabasePath);
                        await vm.OpenDatabaseAsync(vm.SettingsService.Settings.LastDatabasePath);
                    }
                    else
                    {
                        // Load initial schema for default temp database
                        await vm.SchemaExplorer.LoadAsync();
                    }

                    if (sqlFileToLoad is not null)
                    {
                        Log.Information("Loading SQL file from command-line: {Path}", sqlFileToLoad);
                        vm.SqlText = await File.ReadAllTextAsync(sqlFileToLoad);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error during window initialization or processing command-line arguments");
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
