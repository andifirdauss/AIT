using AIThread.Common.Localization;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Data.Memory;

// Opens the database that holds long memory: the main database (Default, migrated with the rest of AIThread) or an
// external one chosen in the Setting menu, whose LongMemories table is created on first use.
public sealed class LongMemoryStore(IDbContextFactory<AIThreadDbContext> mainFactory)
{
    private volatile Backend _current = Backend.Create(MemoryStoreConnection.Default);

    public MemoryStoreConnection Connection => _current.Connection;

    // False when long memory lives in the main database, where deleting a project cascades to its memories.
    public bool IsExternal => _current.Options is not null;

    public string Description => _current.Connection.Provider == MemoryStoreProvider.Default
        ? Lang.T("Database utama AIT", "Main AIT database")
        : _current.Connection.Describe();

    public Task<ILongMemoryDb> CreateAsync(CancellationToken cancellationToken = default) => _current.OpenAsync(mainFactory, cancellationToken);

    // The module registry, code map and merge log, kept in the same database as long memory.
    public Task<IMemoryCatalogDb> CreateCatalogAsync(CancellationToken cancellationToken = default) => _current.OpenCatalogAsync(mainFactory, cancellationToken);

    // The unit test catalog of the projects, kept in the same database as long memory.
    public Task<IUnitTestDb> CreateUnitTestsAsync(CancellationToken cancellationToken = default) => _current.OpenUnitTestsAsync(mainFactory, cancellationToken);

    // The test accounts and testing settings of the projects, kept in the same database as long memory.
    public Task<ITestingDb> CreateTestingAsync(CancellationToken cancellationToken = default) => _current.OpenTestingAsync(mainFactory, cancellationToken);

    // Invalid settings throw before anything changes.
    public void Configure(MemoryStoreConnection connection)
    {
        if (connection == _current.Connection) return;
        _current = Backend.Create(connection);
    }

    // A database for the given settings that is not put in use, e.g. to copy memories into it.
    public Task<ILongMemoryDb> OpenAsync(MemoryStoreConnection connection, CancellationToken cancellationToken = default) =>
        (connection == _current.Connection ? _current : Backend.Create(connection)).OpenAsync(mainFactory, cancellationToken);

    // The catalog next to long memory in a database that is not put in use, e.g. to copy it there.
    public Task<IMemoryCatalogDb> OpenCatalogAsync(MemoryStoreConnection connection, CancellationToken cancellationToken = default) =>
        (connection == _current.Connection ? _current : Backend.Create(connection)).OpenCatalogAsync(mainFactory, cancellationToken);

    // The unit test catalog next to long memory in a database that is not put in use, e.g. to copy it there.
    public Task<IUnitTestDb> OpenUnitTestsAsync(MemoryStoreConnection connection, CancellationToken cancellationToken = default) =>
        (connection == _current.Connection ? _current : Backend.Create(connection)).OpenUnitTestsAsync(mainFactory, cancellationToken);

    // The testing data next to long memory in a database that is not put in use, e.g. to copy it there.
    public Task<ITestingDb> OpenTestingAsync(MemoryStoreConnection connection, CancellationToken cancellationToken = default) =>
        (connection == _current.Connection ? _current : Backend.Create(connection)).OpenTestingAsync(mainFactory, cancellationToken);

    private sealed class Backend(
        MemoryStoreConnection connection,
        DbContextOptions<LongMemoryDbContext>? options,
        DbContextOptions<MemoryCatalogDbContext>? catalogOptions,
        DbContextOptions<UnitTestDbContext>? unitTestOptions,
        DbContextOptions<TestingDbContext>? testingOptions)
    {
        private readonly SemaphoreSlim _schemaLock = new(1, 1);
        private volatile bool _schemaReady;
        private volatile bool _catalogReady;
        private volatile bool _unitTestsReady;
        private volatile bool _testingReady;

        public MemoryStoreConnection Connection => connection;
        public DbContextOptions<LongMemoryDbContext>? Options => options;

        public static Backend Create(MemoryStoreConnection connection) => connection.Provider == MemoryStoreProvider.Default
            ? new Backend(connection, null, null, null, null)
            : new Backend(connection, connection.BuildDbContextOptions<LongMemoryDbContext>(), connection.BuildDbContextOptions<MemoryCatalogDbContext>(),
                connection.BuildDbContextOptions<UnitTestDbContext>(), connection.BuildDbContextOptions<TestingDbContext>());

        // A context of its own, so an external store made by a release without testing data gets these tables on first
        // use (creating the unit test catalog's tables again would fail).
        public async Task<ITestingDb> OpenTestingAsync(IDbContextFactory<AIThreadDbContext> mainFactory, CancellationToken cancellationToken)
        {
            if (testingOptions is null) return await mainFactory.CreateDbContextAsync(cancellationToken);

            var db = new TestingDbContext(testingOptions);
            if (_testingReady) return db;

            await _schemaLock.WaitAsync(cancellationToken);
            try
            {
                if (!_testingReady)
                {
                    // The tables are created together, so probing one tells whether they exist.
                    await MemorySchema.EnsureAsync(connection, db, (c, ct) => c.TestAccounts.AnyAsync(ct), cancellationToken);
                    _testingReady = true;
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

        // A context of its own, so an external store made by a release without the catalog gets these tables on first use
        // (creating the module list's tables again would fail).
        public async Task<IUnitTestDb> OpenUnitTestsAsync(IDbContextFactory<AIThreadDbContext> mainFactory, CancellationToken cancellationToken)
        {
            if (unitTestOptions is null) return await mainFactory.CreateDbContextAsync(cancellationToken);

            var db = new UnitTestDbContext(unitTestOptions);
            if (_unitTestsReady) return db;

            await _schemaLock.WaitAsync(cancellationToken);
            try
            {
                if (!_unitTestsReady)
                {
                    // The three tables are created together, so probing one tells whether they exist.
                    await MemorySchema.EnsureAsync(connection, db, (c, ct) => c.UnitTestCases.AnyAsync(ct), cancellationToken);
                    _unitTestsReady = true;
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

        public async Task<IMemoryCatalogDb> OpenCatalogAsync(IDbContextFactory<AIThreadDbContext> mainFactory, CancellationToken cancellationToken)
        {
            if (catalogOptions is null) return await mainFactory.CreateDbContextAsync(cancellationToken);

            var db = new MemoryCatalogDbContext(catalogOptions);
            if (_catalogReady) return db;

            await _schemaLock.WaitAsync(cancellationToken);
            try
            {
                if (!_catalogReady)
                {
                    // The three tables are created together, so probing one tells whether they exist.
                    await MemorySchema.EnsureAsync(connection, db, (c, ct) => c.ProjectModules.AnyAsync(ct), cancellationToken);
                    _catalogReady = true;
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

        public async Task<ILongMemoryDb> OpenAsync(IDbContextFactory<AIThreadDbContext> mainFactory, CancellationToken cancellationToken)
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
                    await MemorySchema.EnsureColumnsAsync<LongMemory>(db, cancellationToken,
                        [nameof(LongMemory.ProjectKey), nameof(LongMemory.ProjectName), .. MemorySchema.LabelColumns]);
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
