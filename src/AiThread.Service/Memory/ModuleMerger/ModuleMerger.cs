using System.Text.Json;
using AiThread.Data.Cache;
using AiThread.Data.Memory;
using AiThread.Data.Messaging;
using AiThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

// What a merge rewrote, so an undo restores exactly those rows.
public sealed record ModuleMergeMoves(
    List<Guid> LongMemories,
    List<Guid> ShortMemories,
    List<Guid> CodeMap,
    List<Guid> Pages,
    List<string> AddedAliases,
    List<string> SkippedPages);

// Folds a module into another (memories, code map, business pages and the registry follow) and finds modules that
// were split by accident: same name written differently is merged right away, the model judges the rest. A judgement
// at or above AutoMergeConfidence is applied (and can be undone); a lower one waits in Memory → Perlu review.
public sealed partial class ModuleMerger(
    LongMemoryStore longStore,
    IShortMemoryStore shortStore,
    BusinessMemoryStore businessStore,
    IModuleRegistry registry,
    IMemoryAi ai,
    IEventBus eventBus,
    IActivityLogService activityLog,
    ILogger<ModuleMerger> logger) : IModuleMerger
{
    public const double AutoMergeConfidence = 0.85;
    public const string AutoDecider = "Auto";

    private const int MaxTokens = 2_000;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
