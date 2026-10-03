using System.Data.Common;
using System.Diagnostics;
using AiThread.Common.Enums;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace AiThread.Data.Memory;

public sealed record MemoryStoreTestResult(bool Success, long LatencyMs, string Message);

// Checks a memory store without putting it in use. Messages are shown in the Setting menu.
public static class MemoryStoreTester
{
    public static async Task<MemoryStoreTestResult> TestAsync(MemoryLevel level, MemoryStoreConnection connection, CancellationToken cancellationToken = default)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            var message = connection.Provider == MemoryStoreProvider.Redis
                ? await TestRedisAsync(connection)
                : await TestRelationalAsync(level, connection, cancellationToken);
            return new MemoryStoreTestResult(true, watch.ElapsedMilliseconds, message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new MemoryStoreTestResult(false, watch.ElapsedMilliseconds, ErrorMessage(ex));
        }
    }

    private static async Task<string> TestRedisAsync(MemoryStoreConnection connection)
    {
        var options = ConfigurationOptions.Parse(connection.BuildRedisConfiguration());
        options.AbortOnConnectFail = true;
        await using var multiplexer = await ConnectionMultiplexer.ConnectAsync(options);
        var ping = await multiplexer.GetDatabase().PingAsync();
        return $"Terhubung (ping {ping.TotalMilliseconds:0} ms).";
    }

    private static async Task<string> TestRelationalAsync(MemoryLevel level, MemoryStoreConnection connection, CancellationToken cancellationToken)
    {
        bool exists;
        if (level == MemoryLevel.Short)
        {
            await using var db = new ShortMemoryDbContext(connection.BuildDbContextOptions<ShortMemoryDbContext>());
            exists = await MemorySchema.TableExistsAsync(connection, db, (c, ct) => c.ShortMemories.AnyAsync(ct), cancellationToken);
        }
        else
        {
            await using var db = new LongMemoryDbContext(connection.BuildDbContextOptions<LongMemoryDbContext>());
            exists = await MemorySchema.TableExistsAsync(connection, db, (c, ct) => c.LongMemories.AnyAsync(ct), cancellationToken);
        }

        var table = level == MemoryLevel.Short ? "ShortMemories" : "LongMemories";
        return exists
            ? $"Terhubung — tabel {table} sudah ada."
            : $"Terhubung — database/tabel {table} belum ada dan akan dibuat saat setting disimpan.";
    }

    // EF and Redis wrap the provider error ("password authentication failed", "No such host is known").
    public static string ErrorMessage(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is DbException) return e.Message;
        return ex.GetBaseException().Message;
    }
}
