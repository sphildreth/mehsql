using System;
using System.IO;
using MehSql.App.Services;
using Xunit;

namespace MehSql.App.Tests;

public sealed class SettingsServiceTests : IDisposable
{
    private readonly string _tempConfigRoot;
    private readonly string? _originalXdgConfigHome;

    public SettingsServiceTests()
    {
        _originalXdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        _tempConfigRoot = Path.Combine(Path.GetTempPath(), $"mehsql_settings_test_{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _tempConfigRoot);
    }

    [Fact]
    public void AddRecentSqlFile_TracksMostRecentFirst()
    {
        var service = new SettingsService();
        var file1 = "/tmp/first.sql";
        var file2 = "/tmp/second.sql";

        service.AddRecentSqlFile(file1);
        service.AddRecentSqlFile(file2);
        service.AddRecentSqlFile(file1);

        Assert.Equal(Path.GetFullPath(file1), service.Settings.RecentSqlFiles[0]);
        Assert.Equal(Path.GetFullPath(file2), service.Settings.RecentSqlFiles[1]);
    }

    [Fact]
    public void AddQueryHistory_KeepsPerDatabaseBoundedList()
    {
        var service = new SettingsService();
        service.Settings.QueryHistoryLimit = 2;

        service.AddQueryHistory("/tmp/db1.ddb", "SELECT 1;");
        service.AddQueryHistory("/tmp/db1.ddb", "SELECT 2;");
        service.AddQueryHistory("/tmp/db1.ddb", "SELECT 3;");

        var history = service.GetQueryHistory("/tmp/db1.ddb");
        Assert.Equal(2, history.Count);
        Assert.Equal("SELECT 3;", history[0].Sql);
        Assert.Equal("SELECT 2;", history[1].Sql);
    }

    [Fact]
    public void Theme_PersistsAcrossSettingsReload()
    {
        var service = new SettingsService();
        service.Settings.Theme = ThemeMode.Light.ToString();
        service.Save();

        var reloaded = new SettingsService();
        Assert.Equal(ThemeMode.Light.ToString(), reloaded.Settings.Theme);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _originalXdgConfigHome);
        try
        {
            if (Directory.Exists(_tempConfigRoot))
            {
                Directory.Delete(_tempConfigRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
