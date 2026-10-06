using System.Collections.Concurrent;
using AiThread.Common.DTOs;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Data.Cache;
using AiThread.Data.Context;
using AiThread.Data.Memory;
using AiThread.Data.Messaging;
using AiThread.Service;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AiThread.Tests;

// The real service layer and orchestrator over a throwaway SQLite database and project folder. Only 9router
// (ScriptedRouter) and the memory stores (RecordingMemoryService) are fakes; hosted workers are never started.
public sealed class OrchestrationHarness : IAsyncDisposable
{
    private readonly string _dataDir;
    private readonly TestLifetime _lifetime = new();
    private readonly ServiceProvider _services;

    private OrchestrationHarness(Action<SystemSetting>? configureSettings)
    {
        Project = new TempWorkspace();
        _dataDir = Path.Combine(Path.GetTempPath(), "aithread-tests", "data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);
        ConfigureSettings = configureSettings;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AiThread:SkillsDirectory"] = Path.Combine(_dataDir, "skills") })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContextFactory<AiThreadDbContext>(options =>
            options.UseSqlite($"Data Source={Path.Combine(_dataDir, "aithread.db")};Pooling=False"));
        services.AddSingleton<IEventBus, InProcessEventBus>();
        services.AddSingleton<LongMemoryStore>();
        services.AddSingleton<BusinessMemoryStore>();
        services.AddSingleton<IShortMemoryStore, EmptyShortMemoryStore>();
        services.AddSingleton<IHostApplicationLifetime>(_lifetime);
        services.AddServiceLayer(configuration);

        // Registered after the service layer, so these win.
        services.AddSingleton<INineRouterService>(Router);
        services.AddSingleton<IMemoryService>(Memory);
        _services = services.BuildServiceProvider();
    }

    public TempWorkspace Project { get; }

    public Guid ProjectId { get; private set; }

    public ScriptedRouter Router { get; } = new();

    public RecordingMemoryService Memory { get; } = new();

    private Action<SystemSetting>? ConfigureSettings { get; }

    // Build is off by default so tests never spawn a compiler; reviewer-driven verification still runs.
    public static async Task<OrchestrationHarness> CreateAsync(Action<SystemSetting>? configureSettings = null)
    {
        var harness = new OrchestrationHarness(configureSettings);
        await harness.SeedAsync();
        return harness;
    }

    public IThreadOrchestrator Orchestrator => _services.GetRequiredService<IThreadOrchestrator>();

    public ThreadRollback Rollback => _services.GetRequiredService<ThreadRollback>();

    public INodeService Nodes => _services.GetRequiredService<INodeService>();

    public async Task<AiNode> AddNodeAsync(NodeRole role, string model, int priority = 100, bool isActive = true, Guid? fallbackNodeId = null)
    {
        var node = new AiNode
        {
            Name = $"{role}-{model}", Role = role, Model = model, Priority = priority, MaxConcurrency = 4,
            IsActive = isActive, FallbackNodeId = fallbackNodeId
        };
        await using var db = await CreateDbAsync();
        db.Nodes.Add(node);
        await db.SaveChangesAsync();
        return node;
    }

    // Planner, executor and wrapper: the minimum for a thread that changes files.
    public async Task AddStandardNodesAsync()
    {
        await AddNodeAsync(NodeRole.Planner, "planner-model");
        await AddNodeAsync(NodeRole.Executor, "executor-model");
        await AddNodeAsync(NodeRole.Wrapper, "wrapper-model");
    }

    public async Task<(ThreadResult Result, RecordingObserver Observer)> RunAsync(
        string prompt, ThreadSource source = ThreadSource.Dashboard, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        var observer = new RecordingObserver();
        var request = new ThreadRequest(source, prompt, prompt, null, Project.Root, ProjectId, sessionId);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var result = await Orchestrator.RunAsync(request, observer, timeout.Token);
        return (result, observer);
    }

    public async Task<(ThreadResult Result, RecordingObserver Observer)> RetryAsync(Guid threadId)
    {
        var observer = new RecordingObserver();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await Orchestrator.RetryAsync(threadId, observer, timeout.Token);
        return (result, observer);
    }

    public async Task<ThreadRun> LoadThreadAsync(Guid threadId)
    {
        await using var db = await CreateDbAsync();
        return await db.Threads.AsNoTracking().Include(t => t.Tasks).SingleAsync(t => t.Id == threadId);
    }

    public string ReadFile(string relativePath) =>
        File.ReadAllText(Path.Combine(Project.Root, relativePath.Replace('/', Path.DirectorySeparatorChar))).ReplaceLineEndings("\n");

    // Any service of the real service layer, e.g. IProjectService or BusinessDigestRunner.
    public T Service<T>() where T : notnull => _services.GetRequiredService<T>();

    public Task<AiThreadDbContext> CreateDbAsync() =>
        _services.GetRequiredService<IDbContextFactory<AiThreadDbContext>>().CreateDbContextAsync();

    public async ValueTask DisposeAsync()
    {
        _lifetime.Stop();
        await _services.DisposeAsync();
        Project.Dispose();
        try
        {
            Directory.Delete(_dataDir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort; the OS temp folder is cleaned eventually.
        }
    }

    private async Task SeedAsync()
    {
        await using var db = await CreateDbAsync();
        await db.Database.EnsureCreatedAsync();

        var settings = new SystemSetting { Id = SystemSetting.SingletonId, BuildEnabled = false };
        ConfigureSettings?.Invoke(settings);
        db.SystemSettings.Add(settings);

        var project = new Project { Name = "Demo", RootPath = Project.Root };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        ProjectId = project.Id;
    }

    private sealed class TestLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() => Stop();

        public void Stop() => _stopping.Cancel();
    }

    private sealed class EmptyShortMemoryStore : IShortMemoryStore
    {
        public bool IsConnected => true;
        public Task SaveAsync(ShortMemoryRecord record) => Task.CompletedTask;
        public Task<ShortMemoryRecord?> GetAsync(Guid id) => Task.FromResult<ShortMemoryRecord?>(null);
        public Task<IReadOnlyList<ShortMemoryRecord>> GetAllAsync() => Task.FromResult<IReadOnlyList<ShortMemoryRecord>>([]);
        public Task<IReadOnlyList<ShortMemoryRecord>> GetCreatedBeforeAsync(DateTime cutoffUtc) => Task.FromResult<IReadOnlyList<ShortMemoryRecord>>([]);
        public Task<bool> DeleteAsync(Guid id) => Task.FromResult(false);
    }
}

public sealed class RecordingObserver : IThreadObserver
{
    private readonly ConcurrentQueue<string> _lines = new();
    private readonly System.Text.StringBuilder _final = new();

    public IReadOnlyList<string> Lines => _lines.ToList();

    public string Final
    {
        get
        {
            lock (_final) return _final.ToString();
        }
    }

    public Task OnProgressAsync(string line, CancellationToken cancellationToken)
    {
        _lines.Enqueue(line);
        return Task.CompletedTask;
    }

    public Task OnFinalDeltaAsync(string delta, CancellationToken cancellationToken)
    {
        lock (_final) _final.Append(delta);
        return Task.CompletedTask;
    }
}

// Memory as the planner and the extractor see it: an empty index, and every saved memory recorded.
public sealed class RecordingMemoryService : IMemoryService
{
    private readonly ConcurrentQueue<(SaveMemoryRequest Request, Guid? ThreadId)> _saved = new();
    private readonly TaskCompletionSource _firstSave = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyList<(SaveMemoryRequest Request, Guid? ThreadId)> Saved => _saved.ToList();

    public Task WaitForSaveAsync(TimeSpan timeout) => _firstSave.Task.WaitAsync(timeout);

    public Task<MemoryItemDto> AddShortAsync(SaveMemoryRequest request, Guid? sourceThreadId = null, CancellationToken cancellationToken = default)
    {
        _saved.Enqueue((request, sourceThreadId));
        _firstSave.TrySetResult();
        return Task.FromResult(new MemoryItemDto(Guid.NewGuid(), MemoryLevel.Short, request.Type, request.ProjectId, null,
            request.Label, request.Description, sourceThreadId, DateTime.UtcNow, null, 0));
    }

    public Task<IReadOnlyList<MemoryIndexEntry>> GetIndexAsync(Guid? projectId, string? query, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MemoryIndexEntry>>([]);

    public Task<IReadOnlyList<MemoryItemDto>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MemoryItemDto>>([]);

    public Task<PagedResult<MemoryItemDto>> QueryAsync(MemoryQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<MemoryItemDto?> GetAsync(MemoryLevel level, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<MemoryItemDto?> UpdateAsync(MemoryLevel level, Guid id, SaveMemoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(MemoryLevel level, Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> PromoteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<int> ConsolidateAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<MemoryStatsDto> GetStatsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
