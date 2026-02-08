using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MehSql.App.Views;

public partial class AboutDialog : Window
{
    public AboutDialog()
    {
        InitializeComponent();

        var version = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.1.0-alpha";
        VersionText.Text = $"Version {version}";
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
