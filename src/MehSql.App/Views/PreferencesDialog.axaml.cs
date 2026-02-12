using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MehSql.App.Services;

namespace MehSql.App.Views;

public partial class PreferencesDialog : Window
{
    private readonly IThemeManager _themeManager;
    private readonly SettingsService? _settingsService;

    /// <summary>
    /// Required by Avalonia XAML loader.
    /// </summary>
    public PreferencesDialog() : this(new ThemeManager()) { }

    public PreferencesDialog(IThemeManager themeManager)
        : this(themeManager, null)
    {
    }

    public PreferencesDialog(IThemeManager themeManager, SettingsService? settingsService)
    {
        _themeManager = themeManager;
        _settingsService = settingsService;
        InitializeComponent();

        // Set current theme selection
        ThemeCombo.SelectedIndex = _themeManager.CurrentTheme == ThemeMode.Dark ? 0 : 1;

        // Load current temp folder setting
        TempFolderText.Text = _settingsService?.Settings.TempFolder ?? "";
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        var selectedTheme = ThemeCombo.SelectedIndex == 0 ? ThemeMode.Dark : ThemeMode.Light;
        _themeManager.SetTheme(selectedTheme);

        if (_settingsService is not null)
        {
            _settingsService.Settings.Theme = selectedTheme.ToString();
            var tempFolder = TempFolderText.Text?.Trim();
            _settingsService.Settings.TempFolder = string.IsNullOrEmpty(tempFolder) ? null : tempFolder;
            _settingsService.Save();
        }

        Close();
    }

    private async void OnTempBrowseClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Temp Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            TempFolderText.Text = folders[0].Path.LocalPath;
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}
