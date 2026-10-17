using AiThread.Common.Memory;
using AiThread.Common.Options;
using AiThread.Service.Background;
using AiThread.Service.Business;
using AiThread.Service.Implementations;
using AiThread.Service.Implementations.Cli;
using AiThread.Service.Interfaces;
using AiThread.Service.Memory;
using AiThread.Service.Orchestration;
using AiThread.Service.Skills;
using AiThread.Service.Telegram;
using AiThread.Service.Verification;
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
        MemoryLabels.AddAcronyms(configuration.GetSection($"{AiThreadOptions.SectionName}:{nameof(AiThreadOptions.MemoryAcronyms)}").Get<string[]>());
        services.AddDataProtection().SetApplicationName("AiThread");

        // Streaming completions can legitimately run for minutes; timeouts are enforced per call instead.
        services.AddHttpClient(NineRouterService.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

        // The bot token is part of every Telegram URL, so this client must never log request URIs.
        services.AddHttpClient(TelegramApi.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan).RemoveAllLoggers();

        // GitHub's API refuses requests without a User-Agent.
        services.AddHttpClient(SkillFetcher.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AiThread-SkillOrchestrator");
        });

        services.AddScoped<IHealthService, HealthService>();

        // Stateless or self-synchronized services backed by IDbContextFactory: safe as singletons,
        // which the orchestrator and background workers need.
        services.AddSingleton<ISettingService, SettingService>();
        services.AddSingleton<INineRouterService, NineRouterService>();
        services.AddSingleton<INineRouterLauncher, NineRouterLauncher>();
        services.AddSingleton<INodeService, NodeService>();
        services.AddSingleton<ProjectMemoryKeys>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddSingleton<ModuleRegistry>();
        services.AddSingleton<MemoryAi>();
        services.AddSingleton<CodeMapper>();
        services.AddSingleton<ModuleMerger>();
        services.AddSingleton<MemoryRelabeler>();
        services.AddSingleton<IMemoryCatalogService, MemoryCatalogService>();
        services.AddSingleton<ICodeStructureService, CodeStructureService>();
        services.AddSingleton<IMemoryStorageService, MemoryStorageService>();
        services.AddSingleton<IMemoryTransferService, MemoryTransferService>();
        services.AddSingleton<IThreadService, ThreadService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<IClaudeSettingsService, ClaudeSettingsService>();
        services.AddSingleton<ICliIntegration, ClaudeCodeIntegration>();
        services.AddSingleton<ICliIntegration>(sp => new OhMyPiProviderIntegration(sp.GetRequiredService<ISettingService>()));
        services.AddSingleton<ICliIntegration>(sp => new CodexIntegration(sp.GetRequiredService<ISettingService>()));
        services.AddSingleton<ICliIntegration>(sp => new GeminiCliIntegration(sp.GetRequiredService<ISettingService>(), sp.GetRequiredService<IProjectService>()));
        services.AddSingleton<ICliIntegration>(sp => new OpenCodeIntegration(sp.GetRequiredService<ISettingService>()));
        services.AddSingleton<ICliIntegration>(sp => new QwenCodeIntegration(sp.GetRequiredService<ISettingService>()));
        services.AddSingleton<ActivityLogQueue>();
        services.AddSingleton<IActivityLogService, ActivityLogService>();
        services.AddSingleton<IDocsService>(sp => new DocsService(DocsService.ResolveDirectory(
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
            configuration["AiThread:DocsPath"])));

        services.AddSingleton<ThreadRegistry>();
        services.AddSingleton(_ => new PendingQuestionStore(TimeProvider.System));
        services.AddSingleton<NodeConcurrencyLimiter>();
        services.AddSingleton<NodeCaller>();
        services.AddSingleton<ThreadRollback>();
        services.AddSingleton<FileLockProvider>();
        services.AddSingleton<PlannerToolRunner>();
        services.AddSingleton<ToolsNodeAgent>();
        services.AddSingleton<PlannerAgent>();
        services.AddSingleton<ExecutorAgent>();
        services.AddSingleton<FileOperations>();
        services.AddSingleton<ExecutionEngine>();
        services.AddSingleton<BuildRunner>();
        services.AddSingleton<ReviewerAgent>();
        services.AddSingleton<VerificationStage>();
        services.AddSingleton<WrapperAgent>();
        services.AddSingleton<MemoryExtractor>();
        services.AddSingleton<ContextKeeper>();
        services.AddSingleton<SessionHandoff>();
        services.AddSingleton<IThreadOrchestrator, ThreadOrchestrator>();

        services.AddSingleton<TelegramApi>();
        services.AddSingleton<TelegramBotRuntime>();
        services.AddSingleton<TelegramThreadReporter>();
        services.AddSingleton<TelegramBot>();
        services.AddSingleton<ITelegramService, TelegramService>();

        services.AddSingleton<SkillPaths>();
        services.AddSingleton<NodePromptBuilder>();
        services.AddSingleton<SkillImportQueue>();
        services.AddSingleton<SkillImportProgress>();
        services.AddSingleton<SkillFetcher>();
        services.AddSingleton<SkillAi>();
        services.AddSingleton<SkillCatalogAgent>();
        services.AddSingleton<SkillImporter>();
        services.AddSingleton<ISkillService, SkillService>();
        services.AddSingleton<ISkillOrchestrator, SkillOrchestrator>();

        services.AddSingleton<BusinessPageStore>();
        services.AddSingleton<BusinessDigestCollector>();
        services.AddSingleton<BusinessMemoryWriter>();
        services.AddSingleton<BusinessDigestRunner>();
        services.AddSingleton<IBusinessMemoryService, BusinessMemoryService>();

        // First, so memory is read from the configured stores by everything that starts after it.
        services.AddHostedService<MemoryStorageInitializer>();
        services.AddHostedService<ProjectKeyBackfill>();
        services.AddHostedService<ActivityLogWriter>();
        services.AddHostedService<NineRouterAutoStartWorker>();
        services.AddHostedService<RouterMonitorWorker>();
        services.AddHostedService<MemoryConsolidationWorker>();
        services.AddHostedService<MemoryRelabelWorker>();
        services.AddHostedService<TelegramPollingWorker>();
        services.AddHostedService<SkillImportWorker>();
        services.AddHostedService<BusinessMemoryWorker>();

        // Registered last so it stops first: cancelled threads can still log through ActivityLogWriter.
        services.AddHostedService<ThreadLifecycleWorker>();

        return services;
    }
}
