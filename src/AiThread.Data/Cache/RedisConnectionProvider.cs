using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AiThread.Data.Cache;

// Lazily connects so a missing Redis never blocks startup; the multiplexer keeps retrying in the background.
public sealed class RedisConnectionProvider(string connectionString, ILogger<RedisConnectionProvider> logger) : IDisposable
{
    private readonly Lazy<ConnectionMultiplexer?> _multiplexer = new(() =>
    {
        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 2000;
            options.SyncTimeout = 3000;
            options.AsyncTimeout = 3000;
            return ConnectionMultiplexer.Connect(options);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis configuration is invalid: {ConnectionString}", connectionString);
            return null;
        }
    });

    public bool IsConnected => _multiplexer.Value?.IsConnected == true;

    public IDatabase GetDatabase() =>
        _multiplexer.Value?.GetDatabase() ?? throw new InvalidOperationException("Redis is not configured.");

    public void Dispose()
    {
        if (_multiplexer.IsValueCreated) _multiplexer.Value?.Dispose();
    }
}
