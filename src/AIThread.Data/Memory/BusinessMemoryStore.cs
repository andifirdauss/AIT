using AIThread.Common.Localization;
using AIThread.Common.Enums;
using AIThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Data.Memory;

// Opens the database that holds the business wiki: the main database (Default, migrated with the rest of AIThread)
// or an external one chosen in Setting → Penyimpanan memory → Bisnis, whose tables are created on first use. Point
// every developer's AIThread at the same external database to build one wiki per project from everyone's work.
public sealed class BusinessMemoryStore(IDbContextFactory<AIThreadDbContext> mainFactory)
{
    private volatile Backend _current = new(MemoryStoreConnection.Default, null);

    public MemoryStoreConnection Connection => _current.Connection;

    public bool IsExternal => _current.Options is not null;

    public string Description => _current.Connection.Provider == MemoryStoreProvider.Default
        ? Lang.T("Database utama AIT", "Main AIT database")
        : _current.Connection.Describe();

    public Task<IBusinessMemoryDb> CreateAsync(CancellationToken cancellationToken = default) => _current.OpenAsync(mainFactory, cancellationToken);

    // Invalid settings throw before anything changes.
    public void Configure(MemoryStoreConnection connection)
    {
        if (connection == _current.Connection) return;
        _current = Backend.Create(connection);
    }

    // A database for the given settings that is not put in use, e.g. to copy the wiki into it.
    public Task<IBusinessMemoryDb> OpenAsync(MemoryStoreConnection connection, CancellationToken cancellationToken = default) =>
        (connection == _current.Connection ? _current : Backend.Create(connection)).OpenAsync(mainFactory, cancellationToken);

    private sealed class Backend(MemoryStoreConnection connection, DbContextOptions<BusinessMemoryDbContext>? options)
    {
        private readonly SemaphoreSlim _schemaLock = new(1, 1);
        private volatile bool _schemaReady;

        public MemoryStoreConnection Connection => connection;
        public DbContextOptions<BusinessMemoryDbContext>? Options => options;

        public static Backend Create(MemoryStoreConnection connection) => connection.Provider == MemoryStoreProvider.Default
            ? new Backend(connection, null)
            : new Backend(connection, connection.BuildDbContextOptions<BusinessMemoryDbContext>());

        public async Task<IBusinessMemoryDb> OpenAsync(IDbContextFactory<AIThreadDbContext> mainFactory, CancellationToken cancellationToken)
        {
            if (options is null) return await mainFactory.CreateDbContextAsync(cancellationToken);

            var db = new BusinessMemoryDbContext(options);
            if (_schemaReady) return db;

            await _schemaLock.WaitAsync(cancellationToken);
            try
            {
                if (!_schemaReady)
                {
                    // The three tables are created together, so probing one tells whether they exist.
                    await MemorySchema.EnsureAsync(connection, db, (c, ct) => c.BusinessPages.AnyAsync(ct), cancellationToken);
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
