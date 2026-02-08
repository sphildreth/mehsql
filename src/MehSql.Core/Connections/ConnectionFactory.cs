using System;
using DecentDB.AdoNet;

namespace MehSql.Core.Connections;

/// <summary>
/// Factory for creating DecentDB connections.
/// </summary>
public interface IConnectionFactory
{
    /// <summary>
    /// Creates a new DecentDB connection. Caller is responsible for disposing.
    /// </summary>
    DecentDBConnection CreateConnection();
}

/// <summary>
/// Default implementation that creates connections from a connection string or path.
/// </summary>
public sealed class ConnectionFactory : IConnectionFactory
{
    private readonly string _connectionString;

    public ConnectionFactory(string connectionStringOrPath)
    {
        if (string.IsNullOrWhiteSpace(connectionStringOrPath))
        {
            throw new ArgumentException("Connection string or data source path must be provided.", nameof(connectionStringOrPath));
        }

        // If it's just a path (no '='), convert to connection string format
        _connectionString = connectionStringOrPath.Contains('=')
            ? connectionStringOrPath
            : $"Data Source={connectionStringOrPath}";
    }

    public DecentDBConnection CreateConnection()
    {
        return new DecentDBConnection(_connectionString);
    }
}
