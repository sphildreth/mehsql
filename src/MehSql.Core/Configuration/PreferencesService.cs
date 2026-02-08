using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MehSql.Core.Configuration;

/// <summary>
/// User preferences for the MehSQL application.
/// </summary>
public sealed class UserPreferences
{
    public int DefaultPageSize { get; set; } = 500;
    public bool DarkTheme { get; set; } = false;
    public string? LastDatabasePath { get; set; }
    public bool AutoSaveQueries { get; set; } = true;
    public string DateTimeFormat { get; set; } = "yyyy-MM-dd HH:mm:ss";
    public bool ShowLineNumbersInEditor { get; set; } = true;
    public int EditorFontSize { get; set; } = 12;
}

/// <summary>
/// Service for loading and saving user preferences.
/// </summary>
public interface IPreferencesService
{
    /// <summary>
    /// Gets the current user preferences.
    /// </summary>
    UserPreferences Preferences { get; }

    /// <summary>
    /// Loads preferences from disk.
    /// </summary>
    Task LoadAsync();

    /// <summary>
    /// Saves preferences to disk.
    /// </summary>
    Task SaveAsync();
}

/// <summary>
/// Default implementation of preferences service using JSON file storage.
/// </summary>
public sealed class PreferencesService : IPreferencesService
{
    private readonly string _configPath;
    private UserPreferences _preferences = new();

    public UserPreferences Preferences => _preferences;

    public PreferencesService()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var mehSqlPath = Path.Combine(appDataPath, "MehSql");
        Directory.CreateDirectory(mehSqlPath);
        _configPath = Path.Combine(mehSqlPath, "preferences.json");
    }

    public async Task LoadAsync()
    {
        if (!File.Exists(_configPath))
        {
            _preferences = new UserPreferences();
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_configPath);
            _preferences = JsonSerializer.Deserialize(json, UserPreferencesContext.Default.UserPreferences) ?? new UserPreferences();
        }
        catch
        {
            // If loading fails, use defaults
            _preferences = new UserPreferences();
        }
    }

    public async Task SaveAsync()
    {
        var json = JsonSerializer.Serialize(_preferences, UserPreferencesContext.Default.UserPreferences);
        await File.WriteAllTextAsync(_configPath, json);
    }
}

[JsonSerializable(typeof(UserPreferences))]
internal partial class UserPreferencesContext : JsonSerializerContext
{
}
