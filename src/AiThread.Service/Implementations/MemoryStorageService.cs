using System.Security.Cryptography;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

public sealed class MemoryStorageService(
    IDbContextFactory<AiThreadDbContext> dbFactory,
    IDataProtectionProvider dataProtection,
    SwitchableShortMemoryStore shortStore,
    LongMemoryStore longStore,
    BusinessMemoryStore businessStore,
    ILogger<MemoryStorageService> logger) : IMemoryStorageService
{
    private const int CopyBatchSize = 200;

    private readonly IDataProtector _protector = dataProtection.CreateProtector("AiThread.Settings.MemoryStore.v1");
    private readonly SemaphoreSlim _updateLock = new(1, 1);

    public static string DefaultSqlitePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiThread", "memory.db");

    public async Task<MemoryStorageSettingsDto> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await LoadAsync(cancellationToken);
        return new MemoryStorageSettingsDto(
            ToDto(entity.ShortMemoryStore),
            ToDto(entity.LongMemoryStore),
            new MemoryStoreStatusDto(shortStore.IsConnected, shortStore.Description),
            new MemoryStoreStatusDto(await LongConnectedAsync(cancellationToken), longStore.Description),
            ToDto(entity.BusinessMemoryStore),
            new MemoryStoreStatusDto(await BusinessConnectedAsync(cancellationToken), businessStore.Description));
    }

    public async Task<MemoryStorageUpdateResultDto> UpdateAsync(UpdateMemoryStorageRequest request, CancellationToken cancellationToken = default)
    {
        await _updateLock.WaitAsync(cancellationToken);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var entity = await db.SystemSettings.FirstOrDefaultAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken);
            if (entity is null)
            {
                entity = new SystemSetting { Id = SystemSetting.SingletonId };
                db.SystemSettings.Add(entity);
            }

            var oldShort = ToConnection(entity.ShortMemoryStore);
            var oldLong = ToConnection(entity.LongMemoryStore);
            var oldBusiness = ToConnection(entity.BusinessMemoryStore);
            var (shortSetting, newShort) = Normalize(request.ShortMemory, MemoryLevel.Short, entity.ShortMemoryStore);
            var (longSetting, newLong) = Normalize(request.LongMemory, MemoryLevel.Long, entity.LongMemoryStore);
            var (businessSetting, newBusiness) = request.BusinessMemory is { } business
                ? Normalize(business, MemoryLevel.Business, entity.BusinessMemoryStore)
                : (Copy(entity.BusinessMemoryStore), oldBusiness);

            // Open (and create the table in) every new store before anything is saved, so a typo never leaves
            // AiThread pointing at a store it cannot use.
            IShortMemoryStore? targetShort = null;
            try
            {
                if (newShort != oldShort) targetShort = await OpenShortAsync(newShort);
                if (newLong != oldLong) await EnsureLongAsync(newLong, cancellationToken);
                if (newBusiness != oldBusiness) await EnsureBusinessAsync(newBusiness, cancellationToken);

                var warnings = new List<string>();
                int copiedShort = 0, copiedLong = 0, copiedBusiness = 0;
                if (request.CopyExistingData)
                {
                    if (targetShort is not null) copiedShort = await CopyShortAsync(oldShort, targetShort, warnings);
                    if (newLong != oldLong) copiedLong = await CopyLongAsync(oldLong, newLong, warnings, cancellationToken);
                    if (newBusiness != oldBusiness) copiedBusiness = await CopyBusinessAsync(oldBusiness, newBusiness, warnings, cancellationToken);
                }

                Assign(entity.ShortMemoryStore, shortSetting);
                Assign(entity.LongMemoryStore, longSetting);
                Assign(entity.BusinessMemoryStore, businessSetting);
                entity.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                shortStore.Configure(newShort);
                longStore.Configure(newLong);
                businessStore.Configure(newBusiness);
                logger.LogInformation("Memory storage: short = {Short}, long = {Long}, business = {Business}.",
                    shortStore.Description, longStore.Description, businessStore.Description);

                return new MemoryStorageUpdateResultDto(await GetAsync(cancellationToken), copiedShort, copiedLong, warnings, copiedBusiness);
            }
            finally
            {
                (targetShort as IDisposable)?.Dispose();
            }
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async Task<MemoryStoreTestResultDto> TestAsync(TestMemoryStoreRequest request, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(request.Level)) throw new AppValidationException("Level memory tidak valid.");

        var entity = await LoadAsync(cancellationToken);
        var saved = request.Level switch
        {
            MemoryLevel.Short => entity.ShortMemoryStore,
            MemoryLevel.Business => entity.BusinessMemoryStore,
            _ => entity.LongMemoryStore
        };
        var (_, connection) = Normalize(request.Store, request.Level, saved);

        if (connection.Provider != MemoryStoreProvider.Default)
        {
            var result = await MemoryStoreTester.TestAsync(request.Level, connection, cancellationToken);
            return new MemoryStoreTestResultDto(result.Success, result.LatencyMs, result.Message);
        }

        // The built-in stores: report their live state.
        var started = DateTime.UtcNow;
        var connected = request.Level == MemoryLevel.Short ? IsDefaultShortConnected() : await MainDatabaseConnectedAsync(cancellationToken);
        var latency = (long)(DateTime.UtcNow - started).TotalMilliseconds;
        var name = request.Level == MemoryLevel.Short ? "Garnet bawaan" : "Database utama AiThread";
        return new MemoryStoreTestResultDto(connected, latency, connected ? $"{name} terhubung." : $"{name} tidak terhubung.");
    }

    public async Task ApplySavedAsync(CancellationToken cancellationToken = default)
    {
        SystemSetting entity;
        try
        {
            entity = await LoadAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Memory storage settings could not be read; using the built-in stores.");
            return;
        }

        Apply("short", () => shortStore.Configure(ToConnection(entity.ShortMemoryStore)));
        Apply("long", () => longStore.Configure(ToConnection(entity.LongMemoryStore)));
        Apply("business", () => businessStore.Configure(ToConnection(entity.BusinessMemoryStore)));
        logger.LogInformation("Memory storage: short = {Short}, long = {Long}, business = {Business}.",
            shortStore.Description, longStore.Description, businessStore.Description);

        void Apply(string level, Action configure)
        {
            try
            {
                configure();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The saved {Level} memory store is invalid; using the built-in store.", level);
            }
        }
    }

    // Validates the form of one level and resolves the password to keep.
    private (MemoryStoreSetting Setting, MemoryStoreConnection Connection) Normalize(SaveMemoryStoreRequest request, MemoryLevel level, MemoryStoreSetting saved)
    {
        var levelName = level switch { MemoryLevel.Short => "Short memory", MemoryLevel.Business => "Business memory", _ => "Long memory" };
        var provider = request.Provider;
        if (!Enum.IsDefined(provider)) throw new AppValidationException($"{levelName}: provider tidak valid.");
        if (provider == MemoryStoreProvider.Redis && level != MemoryLevel.Short)
            throw new AppValidationException($"{levelName} tidak bisa disimpan di Redis — pilih database.");

        if (provider == MemoryStoreProvider.Default) return (new MemoryStoreSetting(), MemoryStoreConnection.Default);

        var host = Clean(request.Host);
        var database = Clean(request.Database);
        var username = Clean(request.Username);
        var options = Clean(request.Options);
        string? password = null;

        if (provider == MemoryStoreProvider.Sqlite)
        {
            host = null;
            username = null;
            database = Path.GetFullPath(Environment.ExpandEnvironmentVariables(database ?? DefaultSqlitePath));
        }
        else
        {
            if (host is null) throw new AppValidationException($"{levelName}: host wajib diisi.");
            if (request.Port is { } p && (p < 1 || p > 65535)) throw new AppValidationException($"{levelName}: port harus di antara 1 dan 65535.");

            if (provider == MemoryStoreProvider.Redis)
            {
                if (database is not null && (!int.TryParse(database, out var index) || index < 0))
                    throw new AppValidationException($"{levelName}: database Redis berupa angka (0, 1, 2, …).");
            }
            else if (database is null)
            {
                throw new AppValidationException(provider == MemoryStoreProvider.Oracle
                    ? $"{levelName}: service name Oracle wajib diisi (mis. FREEPDB1)."
                    : $"{levelName}: nama database wajib diisi.");
            }

            if (username is null && provider is MemoryStoreProvider.PostgreSql or MemoryStoreProvider.MySql or MemoryStoreProvider.Oracle)
                throw new AppValidationException($"{levelName}: username wajib diisi.");

            password = request.ClearPassword ? null
                : !string.IsNullOrEmpty(request.Password) ? request.Password
                : Unprotect(saved.PasswordProtected);
        }

        EnsureLength(host, 255, $"{levelName}: host");
        EnsureLength(database, 500, $"{levelName}: database");
        EnsureLength(username, 200, $"{levelName}: username");
        EnsureLength(options, 1000, $"{levelName}: opsi tambahan");

        var port = provider == MemoryStoreProvider.Sqlite ? null : request.Port;
        var connection = new MemoryStoreConnection(provider, host, port, database, username, password, options);
        try
        {
            // Builds the connection string once so an unknown option keyword is reported now.
            if (provider == MemoryStoreProvider.Redis) connection.BuildRedisConfiguration();
            else connection.BuildConnectionString();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException)
        {
            throw new AppValidationException($"{levelName}: opsi tambahan tidak valid — {ex.Message}");
        }

        var setting = new MemoryStoreSetting
        {
            Provider = provider,
            Host = host,
            Port = port,
            Database = database,
            Username = username,
            PasswordProtected = password is null ? null : _protector.Protect(password),
            Options = options
        };
        return (setting, connection);
    }

    private async Task<IShortMemoryStore> OpenShortAsync(MemoryStoreConnection connection)
    {
        var store = shortStore.Open(connection);
        try
        {
            if (connection.Provider == MemoryStoreProvider.Redis)
            {
                var test = await MemoryStoreTester.TestAsync(MemoryLevel.Short, connection);
                if (!test.Success) throw new AppValidationException($"Short memory: tidak dapat terhubung — {test.Message}");
            }
            else
            {
                // Creates the database/table of a relational store.
                await store.GetCreatedBeforeAsync(DateTime.UnixEpoch);
            }
            return store;
        }
        catch (Exception ex) when (ex is not AppValidationException)
        {
            (store as IDisposable)?.Dispose();
            throw new AppValidationException($"Short memory: tidak dapat terhubung — {MemoryStoreTester.ErrorMessage(ex)}");
        }
        catch
        {
            (store as IDisposable)?.Dispose();
            throw;
        }
    }

    private async Task EnsureLongAsync(MemoryStoreConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await longStore.OpenAsync(connection, cancellationToken);
            await db.LongMemories.AnyAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AppValidationException($"Long memory: tidak dapat terhubung — {MemoryStoreTester.ErrorMessage(ex)}");
        }
    }

    private async Task EnsureBusinessAsync(MemoryStoreConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var db = await businessStore.OpenAsync(connection, cancellationToken);
            await db.BusinessPages.AnyAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new AppValidationException($"Business memory: tidak dapat terhubung — {MemoryStoreTester.ErrorMessage(ex)}");
        }
    }

    // Inserts the pages, revisions and images the new store does not have yet (by id). A page whose project key and
    // slug already exist there (the same wiki written by another developer) is left out, with its revisions.
    private async Task<int> CopyBusinessAsync(MemoryStoreConnection from, MemoryStoreConnection to, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            await using var source = await businessStore.OpenAsync(from, cancellationToken);
            await using var target = await businessStore.OpenAsync(to, cancellationToken);

            var pages = await source.BusinessPages.AsNoTracking().ToListAsync(cancellationToken);
            if (pages.Count == 0) return 0;

            var existingIds = (await target.BusinessPages.AsNoTracking().Select(p => p.Id).ToListAsync(cancellationToken)).ToHashSet();
            var existingSlugs = (await target.BusinessPages.AsNoTracking().Select(p => new { p.ProjectKey, p.Slug }).ToListAsync(cancellationToken))
                .Select(p => (p.ProjectKey, p.Slug)).ToHashSet();
            var copy = pages.Where(p => !existingIds.Contains(p.Id) && !existingSlugs.Contains((p.ProjectKey, p.Slug))).ToList();
            var skipped = pages.Count(p => !existingIds.Contains(p.Id)) - copy.Count;
            if (skipped > 0) warnings.Add($"{skipped} halaman bisnis tidak disalin karena store baru sudah punya halaman dengan judul yang sama.");

            foreach (var batch in copy.Chunk(CopyBatchSize))
            {
                var ids = batch.Select(p => p.Id).ToList();
                target.BusinessPages.AddRange(batch);
                target.BusinessPageRevisions.AddRange(await source.BusinessPageRevisions.AsNoTracking().Where(r => ids.Contains(r.PageId)).ToListAsync(cancellationToken));
                await target.SaveChangesAsync(cancellationToken);
            }

            // Images one at a time: each may be several megabytes.
            var existingAssets = (await target.BusinessAssets.AsNoTracking().Select(a => a.Id).ToListAsync(cancellationToken)).ToHashSet();
            var assetIds = await source.BusinessAssets.AsNoTracking().Select(a => a.Id).ToListAsync(cancellationToken);
            foreach (var assetId in assetIds.Where(id => !existingAssets.Contains(id)))
            {
                var asset = await source.BusinessAssets.AsNoTracking().FirstAsync(a => a.Id == assetId, cancellationToken);
                target.BusinessAssets.Add(asset);
                await target.SaveChangesAsync(cancellationToken);
                target.ChangeTracker.Clear();
            }
            return copy.Count;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Business memory could not be copied to the new store.");
            warnings.Add($"Business memory lama tidak dapat disalin (sebagian mungkin sudah tersalin): {MemoryStoreTester.ErrorMessage(ex)}");
            return 0;
        }
    }

    private async Task<bool> BusinessConnectedAsync(CancellationToken cancellationToken)
    {
        if (!businessStore.IsExternal) return await MainDatabaseConnectedAsync(cancellationToken);
        try
        {
            await using var db = await businessStore.CreateAsync(cancellationToken);
            await db.BusinessPages.AnyAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    // Upserts by id, so copying twice (or back and forth) never duplicates.
    private async Task<int> CopyShortAsync(MemoryStoreConnection from, IShortMemoryStore to, List<string> warnings)
    {
        var source = from == shortStore.Connection ? shortStore : shortStore.Open(from);
        try
        {
            var records = await source.GetAllAsync();
            foreach (var record in records) await to.SaveAsync(record);
            return records.Count;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Short memories could not be copied to the new store.");
            warnings.Add($"Short memory lama tidak dapat disalin: {MemoryStoreTester.ErrorMessage(ex)}");
            return 0;
        }
        finally
        {
            if (!ReferenceEquals(source, shortStore)) (source as IDisposable)?.Dispose();
        }
    }

    // Inserts the memories the new store does not have yet; existing rows (same id) are left alone.
    private async Task<int> CopyLongAsync(MemoryStoreConnection from, MemoryStoreConnection to, List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            List<LongMemory> rows;
            await using (var source = await longStore.OpenAsync(from, cancellationToken))
                rows = await source.LongMemories.AsNoTracking().ToListAsync(cancellationToken);
            if (rows.Count == 0) return 0;

            // The main database enforces the project foreign key: a project not registered here is dropped from the
            // row, which stays project memory through its key (older rows without a key turn into knowledge).
            HashSet<Guid> projects;
            await using (var main = await dbFactory.CreateDbContextAsync(cancellationToken))
                projects = (await main.Projects.AsNoTracking().Select(p => p.Id).ToListAsync(cancellationToken)).ToHashSet();

            await using var target = await longStore.OpenAsync(to, cancellationToken);
            var existing = (await target.LongMemories.AsNoTracking().Select(m => m.Id).ToListAsync(cancellationToken)).ToHashSet();
            var copied = 0;
            foreach (var batch in rows.Where(r => !existing.Contains(r.Id)).Chunk(CopyBatchSize))
            {
                foreach (var row in batch)
                {
                    var projectId = row.ProjectId is { } pid && projects.Contains(pid) ? pid : (Guid?)null;
                    target.LongMemories.Add(new LongMemory
                    {
                        Id = row.Id,
                        ProjectId = projectId,
                        ProjectKey = row.ProjectKey,
                        ProjectName = row.ProjectName,
                        Type = projectId is null && row.ProjectKey is null ? MemoryType.Knowledge : row.Type,
                        Label = row.Label,
                        Description = row.Description,
                        SourceThreadId = row.SourceThreadId,
                        PromotedAt = row.PromotedAt,
                        HitCount = row.HitCount,
                        LastAccessedAt = row.LastAccessedAt,
                        CreatedAt = row.CreatedAt,
                        UpdatedAt = row.UpdatedAt
                    });
                }
                await target.SaveChangesAsync(cancellationToken);
                copied += batch.Length;
            }
            return copied;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Long memories could not be copied to the new store.");
            warnings.Add($"Long memory lama tidak dapat disalin (sebagian mungkin sudah tersalin): {MemoryStoreTester.ErrorMessage(ex)}");
            return 0;
        }
    }

    private async Task<bool> LongConnectedAsync(CancellationToken cancellationToken)
    {
        if (!longStore.IsExternal) return await MainDatabaseConnectedAsync(cancellationToken);
        try
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            await db.LongMemories.AnyAsync(cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task<bool> MainDatabaseConnectedAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken);
    }

    private bool IsDefaultShortConnected()
    {
        if (shortStore.Connection.Provider == MemoryStoreProvider.Default) return shortStore.IsConnected;
        var store = shortStore.Open(MemoryStoreConnection.Default);
        return store.IsConnected;
    }

    private async Task<SystemSetting> LoadAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        return await db.SystemSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == SystemSetting.SingletonId, cancellationToken)
            ?? new SystemSetting { Id = SystemSetting.SingletonId };
    }

    private MemoryStoreConnection ToConnection(MemoryStoreSetting s) => s.Provider == MemoryStoreProvider.Default
        ? MemoryStoreConnection.Default
        : new MemoryStoreConnection(s.Provider, s.Host, s.Port, s.Database, s.Username, Unprotect(s.PasswordProtected), s.Options);

    private static MemoryStoreDto ToDto(MemoryStoreSetting s) =>
        new(s.Provider, s.Host, s.Port, s.Database, s.Username, !string.IsNullOrEmpty(s.PasswordProtected), s.Options);

    private static MemoryStoreSetting Copy(MemoryStoreSetting s) => new()
    {
        Provider = s.Provider,
        Host = s.Host,
        Port = s.Port,
        Database = s.Database,
        Username = s.Username,
        PasswordProtected = s.PasswordProtected,
        Options = s.Options
    };

    private static void Assign(MemoryStoreSetting target, MemoryStoreSetting source)
    {
        target.Provider = source.Provider;
        target.Host = source.Host;
        target.Port = source.Port;
        target.Database = source.Database;
        target.Username = source.Username;
        target.PasswordProtected = source.PasswordProtected;
        target.Options = source.Options;
    }

    private string? Unprotect(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        try
        {
            return _protector.Unprotect(value);
        }
        catch (CryptographicException ex)
        {
            // Data Protection keys were rotated or deleted; the password has to be entered again.
            logger.LogWarning(ex, "A stored memory store password can no longer be decrypted.");
            return null;
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureLength(string? value, int max, string name)
    {
        if (value is not null && value.Length > max) throw new AppValidationException($"{name} maksimal {max} karakter.");
    }
}
