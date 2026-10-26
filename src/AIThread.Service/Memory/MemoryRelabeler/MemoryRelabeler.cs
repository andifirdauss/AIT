using System.Text.Json;
using System.Text.Json.Serialization;
using AIThread.Common.Enums;
using AIThread.Data.Cache;
using AIThread.Data.Context;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

// Brings memory written before the structured label (or by an older AIT sharing the store) to the new label, in this
// order: backup (once per label version) → business wiki modules and labels normalized (they seed the module list) →
// per registered project with waiting memory (most first): code map of the files that memory names, then relabeling of
// that memory in batches by the model → the remaining memory (knowledge, projects not registered here) → full code map
// scan of every registered project → duplicate module check. Labels never wait for a full scan.
// Every row is claimed before it is sent (RelabelAttempts, conditional update), so several machines sharing a store
// never label the same row twice; after MaxAttempts failures a row waits in Memory → Perlu review instead.
public sealed partial class MemoryRelabeler(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IShortMemoryStore shortStore,
    LongMemoryStore longStore,
    BusinessMemoryStore businessStore,
    IModuleRegistry registry,
    ICodeMapper codeMapper,
    IModuleMerger merger,
    IMemoryAi ai,
    IMemoryTransferService transfer,
    IProjectService projectService,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<MemoryRelabeler> logger) : IMemoryRelabeler
{
    public const int MaxAttempts = 3;
    private const int BatchSize = 20;
    private const int MaxBatchesPerRun = 25;
    private const int ScanMaxFiles = 3_000;
    private const int MaxTokens = 6_000;

    private static readonly JsonSerializerOptions BackupJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(3);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile string? _phase;
    private volatile string? _detail;
    private volatile string? _lastMessage;
    private volatile string? _backupPath;
    private DateTime? _lastRunAt;
    private DateTime _lastProgressAt;
    private int _batchesLeft;

    // Keys whose code map scan finished in this process (touched only inside the gate). A scan cut off by a restart is
    // resumed on the next pass: it only sends the files without an entry to the model.
    private readonly HashSet<string> _scannedKeys = new(StringComparer.Ordinal);

    public static string BackupDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIThread", "backup");

    private sealed record RegisteredProject(Guid Id, string Key, string Name);

    private sealed record Candidate(Guid Id, MemoryLevel Level, MemoryType Type, string? Key, string? ProjectName, string Label, string Description, int Attempts);

    private sealed record NewLabel(string Module, IReadOnlyList<string> ExtraModules, IReadOnlyList<MemoryLayer> Layers, MemoryKind Kind, string? Area,
        IReadOnlyList<string> Keywords);
}
