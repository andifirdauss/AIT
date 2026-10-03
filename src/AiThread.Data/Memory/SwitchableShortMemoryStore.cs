using AiThread.Common.Enums;
using AiThread.Data.Cache;
using Microsoft.Extensions.Logging;

namespace AiThread.Data.Memory;

// The IShortMemoryStore everyone uses. It forwards to the store chosen in the Setting menu, which can be swapped at
// runtime: the built-in Garnet/Redis at ConnectionStrings:Redis (Default), an external Redis, or a relational database.
public sealed class SwitchableShortMemoryStore(
    RedisConnectionProvider defaultRedis,
    string defaultRedisConnectionString,
    ILoggerFactory loggerFactory) : IShortMemoryStore, IDisposable
{
    private readonly Lock _gate = new();
    private volatile Backend _current = new(MemoryStoreConnection.Default, new RedisShortMemoryStore(defaultRedis), null);

    public MemoryStoreConnection Connection => _current.Connection;

    // The store in use, named without credentials.
    public string Description => _current.Connection.Provider == MemoryStoreProvider.Default
        ? $"Garnet bawaan · {defaultRedisConnectionString.Split(',')[0]}"
        : _current.Connection.Describe();

    public bool IsConnected => _current.Store.IsConnected;

    public Task SaveAsync(ShortMemoryRecord record) => _current.Store.SaveAsync(record);
    public Task<ShortMemoryRecord?> GetAsync(Guid id) => _current.Store.GetAsync(id);
    public Task<IReadOnlyList<ShortMemoryRecord>> GetAllAsync() => _current.Store.GetAllAsync();
    public Task<IReadOnlyList<ShortMemoryRecord>> GetCreatedBeforeAsync(DateTime cutoffUtc) => _current.Store.GetCreatedBeforeAsync(cutoffUtc);
    public Task<bool> DeleteAsync(Guid id) => _current.Store.DeleteAsync(id);

    // Switches to the given store. Invalid settings throw before anything changes.
    public void Configure(MemoryStoreConnection connection)
    {
        lock (_gate)
        {
            if (connection == _current.Connection) return;

            var next = Create(connection);
            var previous = _current;
            _current = next;

            // Calls still running on the old store finish on their own connection before it is closed.
            if (previous.OwnedRedis is { } redis) _ = Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => redis.Dispose(), TaskScheduler.Default);
        }

        if (_current.Store is SqlShortMemoryStore sql) _ = sql.ProbeAsync();
    }

    // A store for the given settings that is not put in use, e.g. to copy memories into it or to test it.
    // Dispose the result when done.
    public IShortMemoryStore Open(MemoryStoreConnection connection) =>
        connection.Provider == MemoryStoreProvider.Default
            ? new RedisShortMemoryStore(defaultRedis)
            : Create(connection).AsDisposable();

    public void Dispose()
    {
        lock (_gate) _current.OwnedRedis?.Dispose();
    }

    private Backend Create(MemoryStoreConnection connection)
    {
        switch (connection.Provider)
        {
            case MemoryStoreProvider.Default:
                return new Backend(connection, new RedisShortMemoryStore(defaultRedis), null);
            case MemoryStoreProvider.Redis:
                var redis = new RedisConnectionProvider(connection.BuildRedisConfiguration(), loggerFactory.CreateLogger<RedisConnectionProvider>());
                return new Backend(connection, new RedisShortMemoryStore(redis), redis);
            default:
                return new Backend(connection, new SqlShortMemoryStore(connection, loggerFactory.CreateLogger<SqlShortMemoryStore>()), null);
        }
    }

    private sealed record Backend(MemoryStoreConnection Connection, IShortMemoryStore Store, RedisConnectionProvider? OwnedRedis)
    {
        public IShortMemoryStore AsDisposable() => OwnedRedis is null ? Store : new OwningStore(Store, OwnedRedis);
    }

    // Closes the Redis connection of a store returned by Open.
    private sealed class OwningStore(IShortMemoryStore inner, IDisposable owned) : IShortMemoryStore, IDisposable
    {
        public bool IsConnected => inner.IsConnected;
        public Task SaveAsync(ShortMemoryRecord record) => inner.SaveAsync(record);
        public Task<ShortMemoryRecord?> GetAsync(Guid id) => inner.GetAsync(id);
        public Task<IReadOnlyList<ShortMemoryRecord>> GetAllAsync() => inner.GetAllAsync();
        public Task<IReadOnlyList<ShortMemoryRecord>> GetCreatedBeforeAsync(DateTime cutoffUtc) => inner.GetCreatedBeforeAsync(cutoffUtc);
        public Task<bool> DeleteAsync(Guid id) => inner.DeleteAsync(id);
        public void Dispose() => owned.Dispose();
    }
}
