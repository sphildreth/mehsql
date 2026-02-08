using System;
using System.IO;
using Serilog;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MehSql.App.Services;

/// <summary>
/// Loads and saves user settings from a YAML file in the user's config directory.
/// </summary>
public sealed class SettingsService
{
    private const int MaxRecentFiles = 10;
    private readonly string _settingsPath;

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public UserSettings Settings { get; private set; } = new();

    public SettingsService()
    {
        var configDir = GetConfigDirectory();
        _settingsPath = Path.Combine(configDir, "settings.yaml");
        Load();
    }

    /// <summary>
    /// Records a file path as the most recently opened, pushing it to the top of the list.
    /// </summary>
    public void AddRecentFile(string filePath)
    {
        var normalized = Path.GetFullPath(filePath);
        Settings.RecentFiles.RemoveAll(f => string.Equals(f, normalized, StringComparison.OrdinalIgnoreCase));
        Settings.RecentFiles.Insert(0, normalized);

        if (Settings.RecentFiles.Count > MaxRecentFiles)
        {
            Settings.RecentFiles.RemoveRange(MaxRecentFiles, Settings.RecentFiles.Count - MaxRecentFiles);
        }

        Settings.LastDatabasePath = normalized;
        Save();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_settingsPath)!;
            Directory.CreateDirectory(dir);
            var yaml = Serializer.Serialize(Settings);
            File.WriteAllText(_settingsPath, yaml);
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to save settings to {Path}", _settingsPath);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_settingsPath)) return;
            var yaml = File.ReadAllText(_settingsPath);
            Settings = Deserializer.Deserialize<UserSettings>(yaml) ?? new UserSettings();
        }
        catch (Exception ex)
        {
            Log.Logger.Warning(ex, "Failed to load settings from {Path}, using defaults", _settingsPath);
            Settings = new UserSettings();
        }
    }

    private static string GetConfigDirectory()
    {
        // XDG on Linux, AppData on Windows/macOS
        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdg))
        {
            return Path.Combine(xdg, "mehsql");
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "mehsql");
    }
}
