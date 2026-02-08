using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace MehSql.App.Services
{
    public enum ThemeMode
    {
        Light,
        Dark
    }

    public interface IThemeManager
    {
        ThemeMode CurrentTheme { get; }
        void SetTheme(ThemeMode theme);
        void ToggleTheme();
    }

    public class ThemeManager : IThemeManager
    {
        public ThemeMode CurrentTheme { get; private set; } = ThemeMode.Dark;

        public void SetTheme(ThemeMode theme)
        {
            CurrentTheme = theme;
            
            if (Application.Current?.Styles != null)
            {
                var styles = Application.Current.Styles;
                
                // Find and remove existing theme styles
                var themeStylesToRemove = new List<IStyle>();
                for (int i = styles.Count - 1; i >= 0; i--)
                {
                    if (styles[i] is StyleInclude styleInclude)
                    {
                        var source = styleInclude.Source?.ToString();
                        if (source != null && 
                           (source.Contains("DarkThemeWithStyles") || 
                            source.Contains("LightThemeWithStyles")))
                        {
                            themeStylesToRemove.Add(styles[i]);
                        }
                    }
                }

                foreach (var style in themeStylesToRemove)
                {
                    styles.Remove(style);
                }

                // Add the new theme
                var themeUri = theme == ThemeMode.Light 
                    ? new Uri("avares://MehSql.App/Styles/LightThemeWithStyles.axaml")
                    : new Uri("avares://MehSql.App/Styles/DarkThemeWithStyles.axaml");

                var newStyleInclude = new StyleInclude(new Uri("avares://MehSql.App/"))
                {
                    Source = themeUri
                };

                styles.Add(newStyleInclude);
            }
        }

        public void ToggleTheme()
        {
            var newTheme = CurrentTheme == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light;
            SetTheme(newTheme);
        }
    }
}