using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MehSql.App.Services;
using MehSql.App.ViewModels;
using MehSql.App.Views;
using MehSql.Core.Connections;

namespace MehSql.App;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Create a default database path in temp folder
            var dbPath = Path.Combine(Path.GetTempPath(), $"mehsql_{System.Guid.NewGuid()}.db");
            var connectionFactory = new ConnectionFactory(dbPath);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(connectionFactory)
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
