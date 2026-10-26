using AIThread.Common.Memory;
using AIThread.Common.Options;
using AIThread.Service.Background;
using AIThread.Service.Business;
using AIThread.Service.CodeCalls;
using AIThread.Service.Implementations;
using AIThread.Service.Implementations.Cli;
using AIThread.Service.Interfaces;
using AIThread.Service.Memory;
using AIThread.Service.Orchestration;
using AIThread.Service.Security;
using AIThread.Service.Skills;
using AIThread.Service.Telegram;
using AIThread.Service.Testing;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AIThread.Service;

public static class DependencyInjection
{
    public static IServiceCollection AddServiceLayer(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AIThreadOptions>(configuration.GetSection(AIThreadOptions.SectionName));
        MemoryLabels.AddAcronyms(configuration.GetSection($"{AIThreadOptions.SectionName}:{nameof(AIThreadOptions.MemoryAcronyms)}").Get<string[]>());
        // Kept as "AiThread": the name isolates the protection keys, so changing it would lose every saved secret.
        services.AddDataProtection().SetApplicationName("AiThread");

        // Streaming completions can legitimately run for minutes; timeouts are enforced per call instead.
        services.AddHttpClient(NineRouterService.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

        // The bot token is part of every Telegram URL, so this client must never log request URIs.
        services.AddHttpClient(TelegramApi.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan).RemoveAllLoggers();

        // GitHub's API refuses requests without a User-Agent.
        services.AddHttpClient(SkillFetcher.HttpClientName, client =>
        {
            client.Timeout = TimeSpan.FromMinutes(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AIThread-SkillOrchestrator");
        });

        // Node.js for the Playwright driver when none is installed (±36 MB from nodejs.org).
        services.AddHttpClient(TestBrowserLauncher.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(10));

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
        services.AddSingleton<IModuleSummaryWriter, ModuleSummaryWriter>();
        services.AddSingleton<IMemoryCatalogService, MemoryCatalogService>();
        services.AddSingleton<ICodeStructureService, CodeStructureService>();
        services.AddSingleton<IFunctionCallStore, FunctionCallStore>();
        services.AddSingleton<IFunctionCallQueue, FunctionCallQueue>();
        services.AddSingleton<IFunctionCallScanner, FunctionCallScanner>();
        services.AddSingleton<IMemoryStorageService, MemoryStorageService>();
        services.AddSingleton<IMemoryTransferService, MemoryTransferService>();
        services.AddSingleton<IThreadService, ThreadService>();
        services.AddSingleton<IDashboardService, DashboardService>();
        services.AddSingleton<ISetupService, SetupService>();
        services.AddSingleton<ISetupProfileService, SetupProfileService>();
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
            configuration["AIThread:DocsPath"])));

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
        services.AddSingleton<ITestRunner, TestRunner>();
        services.AddSingleton<IUnitTestCatalog, UnitTestCatalog>();
        services.AddSingleton<IUnitTestExecutor, UnitTestExecutor>();
        services.AddSingleton<IUnitTestScanner, UnitTestScanner>();
        services.AddSingleton<IUnitTestRestorer, UnitTestRestorer>();
        services.AddSingleton<IUnitTestStage, UnitTestStage>();
        services.AddSingleton<IUnitTestService, UnitTestService>();
        services.AddSingleton<IReviewerAgent, ReviewerAgent>();
        services.AddSingleton<IVerificationStage, VerificationStage>();
        services.AddSingleton<ITestBrowserLauncher, TestBrowserLauncher>();
        services.AddSingleton<ITestLoginStore>(_ => new TestLoginStore(TimeProvider.System));
        services.AddSingleton<ITestAccountStore, TestAccountStore>();
        services.AddSingleton<ITestAccountService, TestAccountService>();
        services.AddSingleton<IImpersonationDetector, ImpersonationDetector>();
        services.AddSingleton<ITestScenarioService, TestScenarioService>();
        services.AddSingleton<ISecretGuard, SecretGuard>();
        services.AddSingleton<ISecurityService, SecurityService>();
        services.AddSingleton<ITesterAgent, TesterAgent>();
        services.AddSingleton<IAppLauncher, AppLauncher>();
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
