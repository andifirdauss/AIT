using System.Net;
using System.Net.Sockets;
using AIThread.Common.Enums;
using AIThread.Common.Options;
using AIThread.Data.Cache;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIThread.Tests;

public class EmbeddedRedisServerTests
{
    [Theory]
    [InlineData("localhost:6379", 6379)]
    [InlineData("localhost", 6379)]
    [InlineData("127.0.0.1:6391,password=secret", 6391)]
    [InlineData("[::1]:6392", 6392)]
    public void Local_connection_strings_give_the_port_to_host(string connectionString, int expected)
    {
        Assert.True(EmbeddedRedisServer.TryGetLoopbackPort(connectionString, out var port));
        Assert.Equal(expected, port);
    }

    [Theory]
    [InlineData("redis.example.com:6379")]
    [InlineData("10.0.0.5:6379")]
    [InlineData("localhost:6379,unknownOption=1")]
    public void Remote_or_invalid_connection_strings_are_not_hosted(string connectionString)
    {
        Assert.False(EmbeddedRedisServer.TryGetLoopbackPort(connectionString, out _));
    }

    [Fact]
    public async Task Short_memories_survive_a_restart()
    {
        using var workspace = new TempWorkspace();
        var connectionString = $"127.0.0.1:{FreePort()}";
        var options = new EmbeddedRedisOptions { DataDirectory = workspace.Root };
        var old = new ShortMemoryRecord(Guid.NewGuid(), null, MemoryType.Project, "old", "two hours ago", null, DateTime.UtcNow.AddHours(-2));
        var fresh = new ShortMemoryRecord(Guid.NewGuid(), Guid.NewGuid(), MemoryType.Knowledge, "fresh", "just now", Guid.NewGuid(), DateTime.UtcNow);
        var removed = new ShortMemoryRecord(Guid.NewGuid(), null, MemoryType.Project, "removed", "deleted", null, DateTime.UtcNow.AddMinutes(-5));

        using (var server = await StartServerAsync(connectionString, options))
        using (var connection = new RedisConnectionProvider(connectionString, NullLogger<RedisConnectionProvider>.Instance))
        {
            var store = new RedisShortMemoryStore(connection);
            await store.SaveAsync(old);
            await store.SaveAsync(fresh);
            await store.SaveAsync(removed);
            Assert.True(await store.DeleteAsync(removed.Id));
            await server.StopAsync(CancellationToken.None);
        }

        Assert.NotEmpty(Directory.EnumerateFiles(workspace.Root, "*", SearchOption.AllDirectories));

        using (var server = await StartServerAsync(connectionString, options))
        using (var connection = new RedisConnectionProvider(connectionString, NullLogger<RedisConnectionProvider>.Instance))
        {
            var store = new RedisShortMemoryStore(connection);
            Assert.Equal(fresh, await store.GetAsync(fresh.Id));
            Assert.Null(await store.GetAsync(removed.Id));
            Assert.Equal([fresh.Id, old.Id], (await store.GetAllAsync()).Select(r => r.Id));
            Assert.Equal([old.Id], (await store.GetCreatedBeforeAsync(DateTime.UtcNow.AddHours(-1))).Select(r => r.Id));
            await server.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_taken_port_is_left_to_its_owner()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            using var workspace = new TempWorkspace();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;

            using var server = await StartServerAsync($"127.0.0.1:{port}", new EmbeddedRedisOptions { DataDirectory = workspace.Root });
            await server.StopAsync(CancellationToken.None);

            Assert.Empty(Directory.EnumerateFileSystemEntries(workspace.Root));
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task<EmbeddedRedisServer> StartServerAsync(string connectionString, EmbeddedRedisOptions options)
    {
        var server = new EmbeddedRedisServer(connectionString, options, NullLogger<EmbeddedRedisServer>.Instance);
        await server.StartAsync(CancellationToken.None);
        return server;
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
