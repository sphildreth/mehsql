using Avalonia.Styling;
using MehSql.App.Services;
using Xunit;

namespace MehSql.App.Tests;

public sealed class ThemeManagerTests
{
    [Fact]
    public void ToggleTheme_ChangesCurrentTheme()
    {
        var manager = new ThemeManager();

        Assert.Equal(ThemeMode.Dark, manager.CurrentTheme);

        manager.ToggleTheme();
        Assert.Equal(ThemeMode.Light, manager.CurrentTheme);

        manager.ToggleTheme();
        Assert.Equal(ThemeMode.Dark, manager.CurrentTheme);
    }

    [Fact]
    public void ToThemeVariant_MapsThemeModesCorrectly()
    {
        Assert.Equal(ThemeVariant.Light, ThemeManager.ToThemeVariant(ThemeMode.Light));
        Assert.Equal(ThemeVariant.Dark, ThemeManager.ToThemeVariant(ThemeMode.Dark));
    }

    [Fact]
    public void ParseThemeMode_ReturnsParsedTheme_WhenValueIsValid()
    {
        Assert.Equal(ThemeMode.Light, ThemeManager.ParseThemeMode("Light"));
        Assert.Equal(ThemeMode.Dark, ThemeManager.ParseThemeMode("dark"));
    }

    [Fact]
    public void ParseThemeMode_ReturnsDark_WhenValueIsInvalid()
    {
        Assert.Equal(ThemeMode.Dark, ThemeManager.ParseThemeMode(null));
        Assert.Equal(ThemeMode.Dark, ThemeManager.ParseThemeMode(string.Empty));
        Assert.Equal(ThemeMode.Dark, ThemeManager.ParseThemeMode("not-a-theme"));
    }
}
