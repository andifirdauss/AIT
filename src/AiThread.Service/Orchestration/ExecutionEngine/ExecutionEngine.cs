using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed record ExecutionRequest(
    ThreadPlan Plan,
    IReadOnlyList<ThreadTask> TaskEntities,
    IReadOnlyList<AiNode> Executors,
    WorkspaceScope Scope,
    RuntimeSettings Settings,
    // Null when the changes need not be undoable (no thread to undo).
    FileJournal? Journal = null,
    // Fix tasks of a verification round: their progress lines sit under the round's entry instead of being entries.
    bool Nested = false);

public sealed record TaskResult(string TaskKey, string File, ThreadTaskStatus Status, string? Summary, string? Diff, string? Error, long DurationMs, int Tokens);

public sealed record ExecutionSummary(IReadOnlyList<TaskResult> Results, long WallMs, long SumMs, int Tokens);

// Runs the task DAG: every task whose dependencies succeeded starts immediately, so the stage takes as long as
// the slowest chain — not the sum of all tasks. Tasks whose dependency failed are skipped, not attempted.
// Delete and move tasks are done by FileOperations without a model.
public sealed partial class ExecutionEngine(IExecutorAgent executor, INodeConcurrencyLimiter limiter, IFileOperations fileOperations) : IExecutionEngine
{
    private const string NoModel = "tanpa model";

    private int _roundRobin;

    // One progress line per finished task, in columns sized for the whole batch: icon, id, seconds, executor, text.
    private sealed record TaskLine(int IdWidth, int NodeWidth, bool Nested)
    {
        private const int SecondsWidth = 6;

        public static TaskLine For(ExecutionRequest request) => new(
            request.Plan.Tasks.Select(t => t.Id.Length).DefaultIfEmpty(2).Max(),
            request.Executors.Select(e => ClientProgress.DisplayWidth(e.Name)).Append(NoModel.Length).Max(),
            request.Nested);

        public Task WriteAsync(ClientProgress progress, string icon, string id, string seconds, string node, string text, CancellationToken cancellationToken)
        {
            var head = $"{icon} {ClientProgress.Pad(id, IdWidth)} {ClientProgress.PadLeft(seconds, SecondsWidth)}  {ClientProgress.Pad(node, NodeWidth)}  ";
            var hang = ClientProgress.DisplayWidth(head);
            var line = head + text.ReplaceLineEndings(" ");
            return Nested
                ? progress.DetailAsync(line, cancellationToken, hang: hang)
                : progress.StepAsync(line, cancellationToken, hang: hang);
        }
    }
}
