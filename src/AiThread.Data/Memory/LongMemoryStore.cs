using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Data.Memory;

// Opens the database that holds long memory: the main database (Default, migrated with the rest of AiThread) or an
// external one chosen in the Setting menu, whose LongMemories table is created on first use.
public sealed class LongMemoryStore(IDbContextFactory<AiThreadDbContext> mainFactory)
{
    private volatile Backend _current = new(MemoryStoreConnection.Default, null);

    public MemoryStoreConnection Connection => _current.Connection;

    // False when long memory lives in the main database, where deleting a project cascades to its memories.
    public bool IsExternal => _current.Options is not null;

    public string Description => _current.Connection.Provider == MemoryStoreProvider.Default
        ? "Database utama AiThread"
        : _current.Connection.Describe();

    public Task<ILongMemoryDb> CreateAsync(CancellationToken cancellationToken = default) => _current.OpenAsync(mainFactory, cancellationToken);

    // Invalid settings throw before anything changes.
    public void Configure(MemoryStoreConnection connection)
    {
        if (connection == _current.Connection) return;
        _current = Backend.Create(connection);
    }

    // A database for the given settings that is not put in use, e.g. to copy memories into it.
    public Task<ILongMemoryDb> OpenAsync(MemoryStoreConnection connection, CancellationToken cancellationToken = default) =>
        (connection == _current.Connection ? _current : Backend.Create(connection)).OpenAsync(mainFactory, cancellationToken);

    private sealed class Backend(MemoryStoreConnection connection, DbContextOptions<LongMemoryDbContext>? options)
    {
        private readonly SemaphoreSlim _schemaLock = new(1, 1);
        private volatile bool _schemaReady;

        public MemoryStoreConnection Connection => connection;
        public DbContextOptions<LongMemoryDbContext>? Options => options;

        public static Backend Create(MemoryStoreConnection connection) => connection.Provider == MemoryStoreProvider.Default
            ? new Backend(connection, null)
            : new Backend(connection, connection.BuildDbContextOptions<LongMemoryDbContext>());

        public async Task<ILongMemoryDb> OpenAsync(IDbContextFactory<AiThreadDbContext> mainFactory, CancellationToken cancellationToken)
        {
            if (options is null) return await mainFactory.CreateDbContextAsync(cancellationToken);

            var db = new LongMemoryDbContext(options);
            if (_schemaReady) return db;

            await _schemaLock.WaitAsync(cancellationToken);
            try
            {
                if (!_schemaReady)
                {
                    await MemorySchema.EnsureAsync(connection, db, (c, ct) => c.LongMemories.AnyAsync(ct), cancellationToken);
                    await MemorySchema.EnsureColumnsAsync<LongMemory>(db, cancellationToken, nameof(LongMemory.ProjectKey), nameof(LongMemory.ProjectName));
                    _schemaReady = true;
                }
                return db;
            }
            catch
            {
                await db.DisposeAsync();
                throw;
            }
            finally
            {
                _schemaLock.Release();
            }
        }
    }
}
