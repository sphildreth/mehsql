using System;
using DecentDB.MicroOrm;

namespace MehSql.Core.Connections;

/// <summary>
/// Factory for creating DecentDB context instances.
/// </summary>
public interface IDbContextFactory
{
    /// <summary>
    /// Creates a new DecentDB context. Caller is responsible for disposing.
    /// </summary>
    DecentDBContext CreateContext();
}

/// <summary>
/// Default implementation that creates contexts from a connection string.
/// </summary>
public sealed class DbContextFactory : IDbContextFactory
{
    private readonly string _connectionStringOrPath;

    public DbContextFactory(string connectionStringOrPath)
    {
        if (string.IsNullOrWhiteSpace(connectionStringOrPath))
        {
            throw new ArgumentException("Connection string or data source path must be provided.", nameof(connectionStringOrPath));
        }

        _connectionStringOrPath = connectionStringOrPath;
    }

    public DecentDBContext CreateContext()
    {
        return new DecentDBContext(_connectionStringOrPath);
    }
}
