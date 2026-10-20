using AiThread.Common.Memory;
using AiThread.Common.Options;
using AiThread.Service.Background;
using AiThread.Service.Business;
using AiThread.Service.CodeCalls;
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
        services.AddSingleton<IProjectMemoryKeys, ProjectMemoryKeys>();
        services.AddSingleton<IProjectService, ProjectService>();
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddSingleton<IModuleRegistry, ModuleRegistry>();
        services.AddSingleton<IMemoryAi, MemoryAi>();
        services.AddSingleton<ICodeMapper, CodeMapper>();
        services.AddSingleton<IModuleMerger, ModuleMerger>();
        services.AddSingleton<IMemoryRelabeler, MemoryRelabeler>();
        services.AddSingleton<IMemoryCatalogService, MemoryCatalogService>();
        services.AddSingleton<ICodeStructureService, CodeStructureService>();
        services.AddSingleton<IFunctionCallStore, FunctionCallStore>();
        services.AddSingleton<IFunctionCallQueue, FunctionCallQueue>();
        services.AddSingleton<IFunctionCallScanner, FunctionCallScanner>();
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
        services.AddSingleton<IActivityLogQueue, ActivityLogQueue>();
        services.AddSingleton<IActivityLogService, ActivityLogService>();
        services.AddSingleton<IDocsService>(sp => new DocsService(DocsService.ResolveDirectory(
            sp.GetRequiredService<IHostEnvironment>().ContentRootPath,
            configuration["AiThread:DocsPath"])));

        services.AddSingleton<IThreadRegistry, ThreadRegistry>();
        services.AddSingleton<IPendingQuestionStore>(_ => new PendingQuestionStore(TimeProvider.System));
        services.AddSingleton<INodeConcurrencyLimiter, NodeConcurrencyLimiter>();
        services.AddSingleton<INodeCaller, NodeCaller>();
        services.AddSingleton<IThreadRollback, ThreadRollback>();
        services.AddSingleton<IFileLockProvider, FileLockProvider>();
        services.AddSingleton<ICodeSkeletonTools, CodeSkeletonTools>();
        services.AddSingleton<IPlannerToolRunner, PlannerToolRunner>();
        services.AddSingleton<IToolsNodeAgent, ToolsNodeAgent>();
        services.AddSingleton<IPlannerAgent, PlannerAgent>();
        services.AddSingleton<IExecutorAgent, ExecutorAgent>();
        services.AddSingleton<IFileOperations, FileOperations>();
        services.AddSingleton<IExecutionEngine, ExecutionEngine>();
        services.AddSingleton<IBuildRunner, BuildRunner>();
        services.AddSingleton<IReviewerAgent, ReviewerAgent>();
        services.AddSingleton<IVerificationStage, VerificationStage>();
        services.AddSingleton<IWrapperAgent, WrapperAgent>();
        services.AddSingleton<IMemoryExtractor, MemoryExtractor>();
        services.AddSingleton<IContextKeeper, ContextKeeper>();
        services.AddSingleton<ISessionHandoff, SessionHandoff>();
        services.AddSingleton<IThreadOrchestrator, ThreadOrchestrator>();

        services.AddSingleton<ITelegramApi, TelegramApi>();
        services.AddSingleton<ITelegramBotRuntime, TelegramBotRuntime>();
        services.AddSingleton<ITelegramThreadReporter, TelegramThreadReporter>();
        services.AddSingleton<ITelegramBot, TelegramBot>();
        services.AddSingleton<ITelegramService, TelegramService>();

        services.AddSingleton<ISkillPaths, SkillPaths>();
        services.AddSingleton<INodePromptBuilder, NodePromptBuilder>();
        services.AddSingleton<ISkillImportQueue, SkillImportQueue>();
        services.AddSingleton<ISkillImportProgress, SkillImportProgress>();
        services.AddSingleton<ISkillFetcher, SkillFetcher>();
        services.AddSingleton<ISkillAi, SkillAi>();
        services.AddSingleton<ISkillCatalogAgent, SkillCatalogAgent>();
        services.AddSingleton<ISkillImporter, SkillImporter>();
        services.AddSingleton<ISkillService, SkillService>();
        services.AddSingleton<ISkillOrchestrator, SkillOrchestrator>();

        services.AddSingleton<IBusinessPageStore, BusinessPageStore>();
        services.AddSingleton<IBusinessDigestCollector, BusinessDigestCollector>();
        services.AddSingleton<IBusinessMemoryWriter, BusinessMemoryWriter>();
        services.AddSingleton<IBusinessDigestRunner, BusinessDigestRunner>();
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
        services.AddHostedService<FunctionCallWorker>();

        // Registered last so it stops first: cancelled threads can still log through ActivityLogWriter.
        services.AddHostedService<ThreadLifecycleWorker>();

        return services;
    }
}
