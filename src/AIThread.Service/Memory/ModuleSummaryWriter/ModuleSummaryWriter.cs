using System.Text.Json;
using System.Text.Json.Serialization;
using AIThread.Common.Entities;
using AIThread.Data.Cache;
using AIThread.Data.Memory;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

// The second half of the change log: once change entries (MemoryKind.Change) reach long memory, the model folds them
// into the module's Summary (MemoryKind.Summary, one per project key and module), the current state a later thread
// reads before the history. Summary.PromotedAt is the watermark: the PromotedAt of the newest entry folded in, so every
// entry promoted after it is still due. Facts written by older versions (Pitfall..Pattern, labeled, with a key) are
// folded the same way after a one-time backup and then deleted: they live on in the summary. A module that ended up
// with several summaries (modules merged) is folded into one. Each pass handles a bounded number of modules; the rest
// waits for the next consolidation round.
public sealed partial class ModuleSummaryWriter(
    LongMemoryStore longStore,
    IShortMemoryStore shortStore,
    IMemoryAi ai,
    IMemoryTransferService transfer,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<ModuleSummaryWriter> logger) : IModuleSummaryWriter
{
    private const int MaxModulesPerRun = 30;
    private const int MaxChangesPerCall = 15;
    private const int MaxFactsPerCall = 40;
    private const int MaxTokens = 6_000;

    // Watermark of a summary that has folded no change entry yet.
    private static readonly DateTime NoWatermark = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions BackupJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    // The pass in progress (modules done of the modules taken) and the last outcome, for GetStatusAsync.
    private volatile string? _module;
    private volatile string? _lastMessage;
    private volatile string? _backupPath;
    private int _done;
    private int _total;
    private DateTime? _lastRunAt;

    public static string BackupPrefix => "memory-before-changelog-";

    // The overall progress of the migration, kept next to the backup so a restart does not start the count again.
    public static string ProgressFile => Path.Combine(MemoryRelabeler.BackupDirectory, "memory-changelog-progress.json");

    // The memory of one module of one project key waiting to be folded.
    private sealed record Pending(string Key, string Module, int Changes, int Facts, int ExtraSummaries);

    private sealed record Folded(LongMemory Summary, int Changes, int Facts);

    // Total: the facts waiting when the migration started (raised when more arrive); migrated = Total - still waiting.
    // The waiting count comes from the store, so the work of other machines sharing it counts too.
    private sealed record MigrationProgress(int Total, DateTime StartedAt);
}
