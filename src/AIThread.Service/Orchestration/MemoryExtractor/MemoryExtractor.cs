using AIThread.Data.Context;
using AIThread.Service.Interfaces;
using AIThread.Service.Memory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

// What one extraction did: the memories saved and the tokens spent; Failed when the model or the store failed.
public sealed record MemoryExtraction(int Saved, int Tokens, bool Failed = false);

// After a thread finishes, writes one entry of the project's change log (MemoryKind.Change) into short memory, like a
// MEMORY.md: the feature that changed, the files the thread really changed (from its file snapshots, never from the
// model), and the validations and flows it changed. A thread that changed no file gets an entry only when the model
// finds something worth keeping (the cause of a bug, a decision). The files are mapped first (waiting for the code map
// model when needed), so the module and layers come from the code map rather than from what the model calls them.
// ModuleSummaryWriter folds the entries into the module's summary once they reach long memory.
public sealed partial class MemoryExtractor(
    INodeCaller caller,
    IMemoryService memoryService,
    IProjectService projectService,
    ICodeMapper codeMapper,
    IModuleRegistry registry,
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IActivityLogService activityLog,
    ISecretGuard secrets,
    ILogger<MemoryExtractor> logger) : IMemoryExtractor
{
    // Change entries of each module shown to the model next to the module's summary, so it does not repeat them.
    private const int ExistingChanges = 2;
    private const int MaxMappedFiles = 60;

    private sealed record ExtractedChange(ChangeLogEntry Entry, string? Module, IReadOnlyList<string> OtherModules, IReadOnlyList<string> Keywords);
}
