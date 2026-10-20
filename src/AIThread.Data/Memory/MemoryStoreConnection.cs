using System.Data.Common;
using AIThread.Common.Enums;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using StackExchange.Redis;

namespace AIThread.Data.Memory;

// Decrypted connection of one memory level (short or long), as edited in the Setting menu.
public sealed record MemoryStoreConnection(
    MemoryStoreProvider Provider,
    string? Host = null,
    int? Port = null,
    string? Database = null,
    string? Username = null,
    string? Password = null,
    string? Options = null)
{
    public static readonly MemoryStoreConnection Default = new(MemoryStoreProvider.Default);

    // Short timeouts: a memory store that cannot be reached falls back quickly instead of stalling a thread.
    private const int ConnectTimeoutSeconds = 5;

    public bool IsRelational => Provider is MemoryStoreProvider.SqlServer or MemoryStoreProvider.PostgreSql
        or MemoryStoreProvider.MySql or MemoryStoreProvider.Oracle or MemoryStoreProvider.Sqlite;

    public static int? DefaultPort(MemoryStoreProvider provider) => provider switch
    {
        MemoryStoreProvider.Redis => 6379,
        MemoryStoreProvider.SqlServer => 1433,
        MemoryStoreProvider.PostgreSql => 5432,
        MemoryStoreProvider.MySql => 3306,
        MemoryStoreProvider.Oracle => 1521,
        _ => null
    };

    public static string ProviderName(MemoryStoreProvider provider) => provider switch
    {
        MemoryStoreProvider.Redis => "Redis",
        MemoryStoreProvider.SqlServer => "SQL Server",
        MemoryStoreProvider.PostgreSql => "PostgreSQL",
        MemoryStoreProvider.MySql => "MySQL",
        MemoryStoreProvider.Oracle => "Oracle",
        MemoryStoreProvider.Sqlite => "SQLite",
        _ => "Bawaan"
    };

    // Where the store lives, without credentials. Shown in the UI and in logs.
    public string Describe()
    {
        var name = ProviderName(Provider);
        return Provider switch
        {
            MemoryStoreProvider.Default => name,
            MemoryStoreProvider.Sqlite => $"{name} · {Database}",
            MemoryStoreProvider.Redis => $"{name} · {Host}:{Port ?? DefaultPort(Provider)}" + (string.IsNullOrWhiteSpace(Database) ? "" : $"/{Database}"),
            _ => $"{name} · {Host}{(Port is { } p ? $":{p}" : "")}/{Database}"
        };
    }

    // ADO.NET connection string of a relational store. Throws ArgumentException for an unknown keyword in Options.
    public string BuildConnectionString()
    {
        DbConnectionStringBuilder builder = Provider switch
        {
            MemoryStoreProvider.SqlServer => SqlServer(),
            MemoryStoreProvider.PostgreSql => new NpgsqlConnectionStringBuilder
            {
                Host = Host,
                Port = Port ?? 5432,
                Database = Database,
                Username = Username,
                Password = Password,
                Timeout = ConnectTimeoutSeconds
            },
            MemoryStoreProvider.MySql => new MySqlConnectionStringBuilder
            {
                Server = Host,
                Port = (uint)(Port ?? 3306),
                Database = Database,
                UserID = Username,
                Password = Password,
                ConnectionTimeout = ConnectTimeoutSeconds
            },
            MemoryStoreProvider.Oracle => new OracleConnectionStringBuilder
            {
                // Easy Connect: host:port/service_name.
                DataSource = $"{Host}:{Port ?? 1521}/{Database}",
                UserID = Username,
                Password = Password,
                ConnectionTimeout = ConnectTimeoutSeconds
            },
            MemoryStoreProvider.Sqlite => new SqliteConnectionStringBuilder { DataSource = Database },
            _ => throw new InvalidOperationException($"{Provider} is not a relational memory store.")
        };

        ApplyOptions(builder);
        return builder.ConnectionString;
    }

    // StackExchange.Redis configuration string of a Redis store.
    public string BuildRedisConfiguration()
    {
        if (Provider != MemoryStoreProvider.Redis) throw new InvalidOperationException($"{Provider} is not a Redis store.");

        var options = new ConfigurationOptions
        {
            User = string.IsNullOrWhiteSpace(Username) ? null : Username,
            Password = string.IsNullOrEmpty(Password) ? null : Password,
            ConnectTimeout = ConnectTimeoutSeconds * 1000
        };
        options.EndPoints.Add(Host ?? "localhost", Port ?? 6379);
        if (int.TryParse(Database, out var index)) options.DefaultDatabase = index;

        var configuration = options.ToString(includePassword: true);
        if (!string.IsNullOrWhiteSpace(Options)) configuration += "," + Options.Trim().Trim(',');

        // Parse once so an invalid option is reported when the setting is saved, not on first use.
        ConfigurationOptions.Parse(configuration);
        return configuration;
    }

    public DbContextOptions<TContext> BuildDbContextOptions<TContext>() where TContext : DbContext
    {
        var connectionString = BuildConnectionString();
        var builder = new DbContextOptionsBuilder<TContext>();
        _ = Provider switch
        {
            MemoryStoreProvider.SqlServer => builder.UseSqlServer(connectionString),
            MemoryStoreProvider.PostgreSql => builder.UseNpgsql(connectionString),
            MemoryStoreProvider.MySql => builder.UseMySQL(connectionString),
            MemoryStoreProvider.Oracle => builder.UseOracle(connectionString),
            MemoryStoreProvider.Sqlite => builder.UseSqlite(connectionString),
            _ => throw new InvalidOperationException($"{Provider} is not a relational memory store.")
        };
        return builder.Options;
    }

    private SqlConnectionStringBuilder SqlServer()
    {
        var builder = new SqlConnectionStringBuilder
        {
            DataSource = Port is { } port ? $"{Host},{port}" : Host,
            InitialCatalog = Database,
            TrustServerCertificate = true,
            ConnectTimeout = ConnectTimeoutSeconds
        };

        // No user name = Windows authentication, as in the default LocalDB connection string.
        if (string.IsNullOrWhiteSpace(Username))
        {
            builder.IntegratedSecurity = true;
        }
        else
        {
            builder.UserID = Username;
            builder.Password = Password ?? string.Empty;
        }
        return builder;
    }

    // Options win over the fields above, so e.g. "Timeout=30" or "SSL Mode=Require" can be set without a new field.
    private void ApplyOptions(DbConnectionStringBuilder builder)
    {
        if (string.IsNullOrWhiteSpace(Options)) return;

        var extra = new DbConnectionStringBuilder { ConnectionString = Options.Trim() };
        foreach (string key in extra.Keys) builder[key] = extra[key];
    }
}
