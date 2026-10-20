using AIThread.Common.Options;
using AIThread.Data.Cache;
using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIThread.Data;

public static class DependencyInjection
{
    public const string DefaultConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=AIThread;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public const string DefaultRedisConnectionString = "localhost:6379";

    // Register DbContext, Redis, message broker, repositories, etc.
    public static IServiceCollection AddDataLayer(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default") ?? DefaultConnectionString;
        var redisConnectionString = configuration.GetConnectionString("Redis") ?? DefaultRedisConnectionString;

        // A factory rather than a scoped context: executors update rows from many parallel tasks.
        services.AddDbContextFactory<AIThreadDbContext>(options =>
            options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(3)));

        var embeddedRedis = configuration
            .GetSection($"{AIThreadOptions.SectionName}:{nameof(AIThreadOptions.EmbeddedRedis)}")
            .Get<EmbeddedRedisOptions>() ?? new EmbeddedRedisOptions();
        if (embeddedRedis.Enabled)
        {
            // Registered before the service layer's workers, so it starts before them and stops after them.
            services.AddHostedService(sp => new EmbeddedRedisServer(
                redisConnectionString, embeddedRedis, sp.GetRequiredService<ILogger<EmbeddedRedisServer>>()));
        }

        services.AddSingleton(sp => new RedisConnectionProvider(
            redisConnectionString, sp.GetRequiredService<ILogger<RedisConnectionProvider>>()));

        // Where short, long and business memory live is a runtime setting (menu Setting); all start on the built-in stores
        // and are switched by the service layer once the setting is loaded.
        services.AddSingleton(sp => new SwitchableShortMemoryStore(
            sp.GetRequiredService<RedisConnectionProvider>(), redisConnectionString, sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IShortMemoryStore>(sp => sp.GetRequiredService<SwitchableShortMemoryStore>());
        services.AddSingleton<LongMemoryStore>();
        services.AddSingleton<BusinessMemoryStore>();

        services.AddSingleton<IEventBus, InProcessEventBus>();

        return services;
    }

    // Applies pending migrations. A database outage is logged instead of crashing the host,
    // so the dashboard can still report what is wrong.
    public static async Task InitializeDataLayerAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("AIThread.Data");
        try
        {
            var factory = services.GetRequiredService<IDbContextFactory<AIThreadDbContext>>();
            await using var db = await factory.CreateDbContextAsync(cancellationToken);
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database is up to date.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed. Check ConnectionStrings:Default.");
        }
    }
}
