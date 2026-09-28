using AiThread.Common.Options;
using AiThread.Service.Background;
using AiThread.Service.Implementations;
using AiThread.Service.Interfaces;
using AiThread.Service.Orchestration;
using AiThread.Service.Workspace;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AiThread.Service;

public static class DependencyInjection
{
    public static IServiceCollection AddServiceLayer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiThreadOptions>(configuration.GetSection(AiThreadOptions.SectionName));
        services.AddDataProtection().SetApplicationName("AiThread");

        // Streaming completions can legitimately run for minutes; timeouts are enforced per call instead.
        services.AddHttpClient(NineRouterService.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

        services.AddScoped<IHealthService, HealthService>();

        // Stateless or self-synchronized services backed by IDbContextFactory: safe as singletons,
        // which the orchestrator and background workers need.
        services.AddSingleton<ISettingService, SettingService>();
        services.AddSingleton<INineRouterService, NineRouterService>();
        services.AddSingleton<INodeService, NodeService>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddSingleton<IThreadService, ThreadService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IClaudeSettingsService, ClaudeSettingsService>();
        services.AddSingleton<ActivityLogQueue>();
        services.AddSingleton<IActivityLogService, ActivityLogService>();
        services.AddSingleton<IDocsService>(sp => new DocsService(DocsService.ResolveDirectory(
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
            configuration["AiThread:DocsPath"])));

        services.AddSingleton<ThreadRegistry>();
        services.AddSingleton<NodeConcurrencyLimiter>();
        services.AddSingleton<FileLockProvider>();
        services.AddSingleton<PlannerAgent>();
        services.AddSingleton<ExecutorAgent>();
        services.AddSingleton<ExecutionEngine>();
        services.AddSingleton<WrapperAgent>();
        services.AddSingleton<MemoryExtractor>();
        services.AddSingleton<IThreadOrchestrator, ThreadOrchestrator>();

        services.AddHostedService<ActivityLogWriter>();
        services.AddHostedService<RouterMonitorWorker>();
        services.AddHostedService<MemoryConsolidationWorker>();

        return services;
    }
}
