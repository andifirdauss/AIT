using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using AIThread.Common.Options;
using Garnet;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace AIThread.Data.Cache;

// Runs Microsoft Garnet (Redis protocol) inside AIThread for short memory. Every write reaches the append-only file
// before it is acknowledged, so a crash loses nothing; a periodic checkpoint keeps that file small.
public sealed class EmbeddedRedisServer(
    string redisConnectionString,
    EmbeddedRedisOptions options,
    ILogger<EmbeddedRedisServer> logger) : BackgroundService
{
    // Logged on every start and harmless: the notice that AIThread's logger replaces Garnet's own, and AOF recovery
    // probing past the end of the file (Windows ERROR_HANDLE_EOF = 38) before it falls back to the commit metadata.
    private static readonly string[] BenignLogPrefixes =
    [
        "Received an external ILoggerFactory object",
        "AsyncReadPagesToFrameCallback error: 38",
        "Wait for frame load was canceled",
        "Fast-commit scan forward from",
    ];

    private GarnetServer? _server;
    private int _port;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (!TryGetLoopbackPort(redisConnectionString, out _port))
        {
            logger.LogInformation(
                "ConnectionStrings:Redis ({ConnectionString}) is not a local address; embedded Garnet is not started.",
                redisConnectionString);
            return Task.CompletedTask;
        }

        if (IsPortInUse(_port))
        {
            logger.LogWarning(
                "Port {Port} is already in use (an old Redis or Docker container?); short memory uses that server instead of embedded Garnet.",
                _port);
            return Task.CompletedTask;
        }

        var dataDirectory = string.IsNullOrWhiteSpace(options.DataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIThread", "garnet", _port.ToString(CultureInfo.InvariantCulture))
            : options.DataDirectory;

        try
        {
            Directory.CreateDirectory(dataDirectory);
            _server = new GarnetServer(BuildArguments(_port, dataDirectory, options.MemorySize), new ForwardingLoggerFactory(logger));
            _server.Start();
        }
        catch (Exception ex)
        {
            _server?.Dispose();
            _server = null;
            logger.LogError(ex, "Embedded Garnet failed to start on port {Port}; new memories go straight to the database.", _port);
            return Task.CompletedTask;
        }

        logger.LogInformation("Embedded Garnet listening on loopback port {Port}, data in {DataDirectory}.", _port, dataDirectory);
        return base.StartAsync(cancellationToken);
    }

    // Periodic checkpoints. The first one also trims what the previous run left behind, since stopping the debugger
    // kills the process without a graceful shutdown.
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.CheckpointIntervalMinutes)));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
            do
            {
                await CheckpointAsync().ConfigureAwait(false);
            } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        _server?.Dispose();
        _server = null;
    }

    public override void Dispose()
    {
        _server?.Dispose();
        _server = null;
        base.Dispose();
    }

    // BGSAVE is an admin command, which the shared application connection does not enable.
    private async Task CheckpointAsync()
    {
        try
        {
            var config = new ConfigurationOptions
            {
                EndPoints = { new IPEndPoint(IPAddress.Loopback, _port) },
                AllowAdmin = true,
                ConnectTimeout = 2000,
            };
            using var connection = await ConnectionMultiplexer.ConnectAsync(config).ConfigureAwait(false);
            await connection.GetServer(connection.GetEndPoints()[0]).SaveAsync(SaveType.BackgroundSave).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Embedded Garnet checkpoint failed.");
        }
    }

    // The embedded server can only stand in for a Redis on this machine.
    public static bool TryGetLoopbackPort(string connectionString, out int port)
    {
        port = 0;
        ConfigurationOptions config;
        try
        {
            config = ConfigurationOptions.Parse(connectionString);
        }
        catch (ArgumentException)
        {
            return false;
        }

        config.SetDefaultPorts();
        switch (config.EndPoints.FirstOrDefault())
        {
            case DnsEndPoint dns when dns.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase):
                port = dns.Port;
                return true;
            case IPEndPoint ip when IPAddress.IsLoopback(ip.Address):
                port = ip.Port;
                return true;
            default:
                return false;
        }
    }

    // Garnet does not fail when the port is taken: Windows lets it bind next to the existing listener.
    private static bool IsPortInUse(int port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(endPoint => endPoint.Port == port);

    private static string[] BuildArguments(int port, string dataDirectory, string memorySize) =>
    [
        "--port", port.ToString(CultureInfo.InvariantCulture),
        // "localhost" resolves to ::1 first.
        "--bind", Socket.OSSupportsIPv6 ? "127.0.0.1 ::1" : "127.0.0.1",
        "--checkpointdir", dataDirectory,
        "--aof",
        "--recover",
        "-q",
        // Sized for a few thousand small keys instead of the multi-GB defaults.
        "--memory", memorySize,
        "--page", "256k",
        "--index", "1m",
        "--aof-memory", "2m",
        "--aof-page-size", "512k",
        // Small segments let a checkpoint delete the old part of the append-only file.
        "--aof-segment-size", "1m",
    ];

    // Funnels Garnet's many log categories into this class's logger, warnings and up.
    private sealed class ForwardingLoggerFactory(ILogger target) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => new ForwardingLogger(target, categoryName);

        public void AddProvider(ILoggerProvider provider)
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ForwardingLogger(ILogger target, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning && target.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var message = formatter(state, exception);
            if (BenignLogPrefixes.Any(prefix => message.StartsWith(prefix, StringComparison.Ordinal))) return;
            target.Log(logLevel, eventId, exception, "Garnet {Category}: {Message}", category, message);
        }
    }
}
