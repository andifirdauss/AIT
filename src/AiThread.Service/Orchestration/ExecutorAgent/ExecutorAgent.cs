using AiThread.Common.Entities;
using AiThread.Common.Options;
using AiThread.Service.Interfaces;
using AiThread.Service.Workspace;
using Microsoft.Extensions.Options;

namespace AiThread.Service.Orchestration;

public sealed record ExecutorJob(
    ThreadPlan Plan,
    PlanTask Task,
    AiNode Node,
    WorkspaceScope Scope,
    int MaxRetries,
    Func<string, Task> OnOutput,
    Func<int, Task> OnAttempt,
    // Records the file before and after each write, so the thread can be undone. Null = not undoable.
    FileJournal? Journal = null,
    // The executor's fallback node took over; later attempts stay on it.
    Func<NodeSwitch, Task>? OnSwitch = null,
    // prompt_cache_key shared by the executors of one thread, whose task messages start with the same plan section.
    string? CacheKey = null);

public sealed record ExecutorOutcome(bool Success, string? Summary, string? Diff, string? Error, int Tokens, int Attempts);

// Cheap model, tiny scope: one file, the instruction, the contract and a few excerpts. Its SEARCH/REPLACE
// output is applied under a per-file lock against the file's current content; failures are fed back for a retry.
// The caller (ExecutionEngine) holds a concurrency slot of job.Node; a fallback node gets its own slot per call.
public sealed partial class ExecutorAgent(INodeCaller caller, IFileLockProvider fileLocks, INodePromptBuilder prompts, IOptions<AiThreadOptions> options) : IExecutorAgent
{
    private const int ExcerptMargin = 40;
    private const int MaxContextLinesPerRef = 250;
    private const int MaxContextChars = 40_000;
}
