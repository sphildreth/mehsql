using Avalonia.Controls;
using Avalonia.Interactivity;
using MehSql.App.Services;

namespace MehSql.App.Views;

public partial class PreferencesDialog : Window
{
    private readonly IThemeManager _themeManager;

    /// <summary>
    /// Required by Avalonia XAML loader.
    /// </summary>
    public PreferencesDialog() : this(new ThemeManager()) { }

    public PreferencesDialog(IThemeManager themeManager)
    {
        _themeManager = themeManager;
        InitializeComponent();

        // Set current theme selection
        ThemeCombo.SelectedIndex = _themeManager.CurrentTheme == ThemeMode.Dark ? 0 : 1;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var selectedTheme = ThemeCombo.SelectedIndex == 0 ? ThemeMode.Dark : ThemeMode.Light;
        _themeManager.SetTheme(selectedTheme);
        Close();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
