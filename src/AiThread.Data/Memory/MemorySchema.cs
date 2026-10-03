using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace AiThread.Data.Memory;

// External memory databases are not migrated: the store creates its own table on first use. EnsureCreated cannot be
// used because it does nothing once the database holds any table (another app's, or the other memory level's).
public static class MemorySchema
{
    public static async Task EnsureAsync<TContext>(
        MemoryStoreConnection connection,
        TContext db,
        Func<TContext, CancellationToken, Task> probeTable,
        CancellationToken cancellationToken) where TContext : DbContext
    {
        PrepareLocalFile(connection);
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken)) await creator.CreateAsync(cancellationToken);

        try
        {
            await probeTable(db, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Table missing. A connection problem fails here again, with the provider's own message.
            await creator.CreateTablesAsync(cancellationToken);
        }
    }

    // True when the table can be read; false when the database or the table does not exist yet.
    // Connection errors (host, login) are thrown.
    public static async Task<bool> TableExistsAsync<TContext>(
        MemoryStoreConnection connection,
        TContext db,
        Func<TContext, CancellationToken, Task> probeTable,
        CancellationToken cancellationToken) where TContext : DbContext
    {
        if (connection.Provider == MemoryStoreProvider.Sqlite && !File.Exists(SqlitePath(connection))) return false;

        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!await creator.ExistsAsync(cancellationToken)) return false;

        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await probeTable(db, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static void PrepareLocalFile(MemoryStoreConnection connection)
    {
        if (connection.Provider != MemoryStoreProvider.Sqlite) return;
        var directory = Path.GetDirectoryName(SqlitePath(connection));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
    }

    private static string SqlitePath(MemoryStoreConnection connection) => Path.GetFullPath(connection.Database ?? string.Empty);
}
