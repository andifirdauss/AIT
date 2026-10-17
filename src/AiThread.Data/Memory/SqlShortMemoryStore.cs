using AiThread.Data.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Data.Memory;

// Short memory in a relational database (table ShortMemories), created on first use.
// IsConnected is the outcome of the last operation or probe; while it is false, reading it re-probes in the
// background at most every ProbeInterval, so the store recovers by itself once the database is back.
public sealed class SqlShortMemoryStore : IShortMemoryStore
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(15);

    private readonly MemoryStoreConnection _connection;
    private readonly DbContextOptions<ShortMemoryDbContext> _options;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _schemaLock = new(1, 1);
    private volatile bool _schemaReady;
    private volatile bool _connected;
    private volatile bool _reportedDown;
    private long _lastProbeTicks;
    private int _probing;

    public SqlShortMemoryStore(MemoryStoreConnection connection, ILogger logger)
    {
        _connection = connection;
        _options = connection.BuildDbContextOptions<ShortMemoryDbContext>();
        _logger = logger;
    }

    public bool IsConnected
    {
        get
        {
            if (!_connected) ProbeInBackground();
            return _connected;
        }
    }

    // Creates the table when needed. Returns false (and logs) when the database cannot be reached.
    public async Task<bool> ProbeAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _lastProbeTicks, DateTime.UtcNow.Ticks);
        try
        {
            await using var db = await OpenAsync(cancellationToken);
            await db.ShortMemories.AnyAsync(cancellationToken);
            _connected = true;
            _reportedDown = false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Logged once per outage, not on every background probe.
            if (!_reportedDown) _logger.LogWarning(ex, "Short memory store {Store} is not reachable.", _connection.Describe());
            _reportedDown = true;
            _connected = false;
        }
        return _connected;
    }

    public Task SaveAsync(ShortMemoryRecord record) => RunAsync(async db =>
    {
        var row = await db.ShortMemories.FirstOrDefaultAsync(r => r.Id == record.Id);
        if (row is null)
        {
            row = new ShortMemoryRow { Id = record.Id };
            db.ShortMemories.Add(row);
        }

        row.ProjectId = record.ProjectId;
        row.ProjectKey = record.ProjectKey;
        row.ProjectName = record.ProjectName;
        row.Type = record.Type;
        row.Label = record.Label;
        row.Description = record.Description;
        row.SourceThreadId = record.SourceThreadId;
        row.CreatedAt = record.CreatedAt;
        row.Module = record.Module;
        row.ExtraModules = record.ExtraModules;
        row.Layers = record.Layers;
        row.Kind = record.Kind;
        row.Area = record.Area;
        row.Keywords = record.Keywords;
        row.LabelVersion = record.LabelVersion;
        row.LegacyLabel = record.LegacyLabel;
        row.ReviewReason = record.ReviewReason;
        row.RelabelAttempts = record.RelabelAttempts;
        await db.SaveChangesAsync();
        return true;
    });

    public Task<ShortMemoryRecord?> GetAsync(Guid id) => RunAsync(async db =>
    {
        var row = await db.ShortMemories.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        return row is null ? null : ToRecord(row);
    });

    public Task<IReadOnlyList<ShortMemoryRecord>> GetAllAsync() => RunAsync<IReadOnlyList<ShortMemoryRecord>>(async db =>
        (await db.ShortMemories.AsNoTracking().OrderByDescending(r => r.CreatedAt).ToListAsync()).Select(ToRecord).ToList());

    public Task<IReadOnlyList<ShortMemoryRecord>> GetCreatedBeforeAsync(DateTime cutoffUtc) => RunAsync<IReadOnlyList<ShortMemoryRecord>>(async db =>
        (await db.ShortMemories.AsNoTracking().Where(r => r.CreatedAt <= cutoffUtc).OrderByDescending(r => r.CreatedAt).ToListAsync())
            .Select(ToRecord).ToList());

    public Task<bool> DeleteAsync(Guid id) => RunAsync(async db =>
        await db.ShortMemories.Where(r => r.Id == id).ExecuteDeleteAsync() > 0);

    private async Task<T> RunAsync<T>(Func<ShortMemoryDbContext, Task<T>> action)
    {
        try
        {
            await using var db = await OpenAsync(CancellationToken.None);
            var result = await action(db);
            _connected = true;
            return result;
        }
        catch
        {
            _connected = false;
            Interlocked.Exchange(ref _lastProbeTicks, DateTime.UtcNow.Ticks);
            throw;
        }
    }

    private async Task<ShortMemoryDbContext> OpenAsync(CancellationToken cancellationToken)
    {
        var db = new ShortMemoryDbContext(_options);
        if (_schemaReady) return db;

        await _schemaLock.WaitAsync(cancellationToken);
        try
        {
            if (!_schemaReady)
            {
                await MemorySchema.EnsureAsync(_connection, db, (c, ct) => c.ShortMemories.AnyAsync(ct), cancellationToken);
                await MemorySchema.EnsureColumnsAsync<ShortMemoryRow>(db, cancellationToken,
                    [nameof(ShortMemoryRow.ProjectKey), nameof(ShortMemoryRow.ProjectName), .. MemorySchema.LabelColumns]);
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

    private void ProbeInBackground()
    {
        var last = new DateTime(Interlocked.Read(ref _lastProbeTicks), DateTimeKind.Utc);
        if (DateTime.UtcNow - last < ProbeInterval) return;
        if (Interlocked.CompareExchange(ref _probing, 1, 0) != 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await ProbeAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _probing, 0);
            }
        });
    }

    private static ShortMemoryRecord ToRecord(ShortMemoryRow r) =>
        new(r.Id, r.ProjectId, r.Type, r.Label, r.Description, r.SourceThreadId, r.CreatedAt, r.ProjectKey, r.ProjectName,
            r.Module, r.Layers, r.Kind, r.Area, r.Keywords, r.LabelVersion, r.LegacyLabel, r.ReviewReason, r.RelabelAttempts, r.ExtraModules);
}
