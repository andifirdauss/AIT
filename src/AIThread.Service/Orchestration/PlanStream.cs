using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

// What a paused planner had already committed (commit_tasks), kept with its conversation so the resumed run goes on
// from there: the committed part of the plan, whether each file existed when its task was committed, the folders
// outside the root allowed for them, and whether they were executed (their results are then in the thread's tasks).
public sealed record CommittedWork(ThreadPlan Plan, IReadOnlyDictionary<string, bool> Existence, IReadOnlyList<string> Folders, int Batches, bool Executed);

// The plan as the planner commits it, batch by batch (commit_tasks), before its final submit_plan. Committed tasks and
// contract items are fixed: later batches only add to them. With early execution a batch goes to the execution
// engine at once and runs while the planner keeps planning; otherwise the batches wait for the final plan and only
// keep each planner answer short. Files are checked as they were when their task was committed (Existence), since an
// earlier batch may already have created, moved or deleted them. One per thread run; the planner's tool calls of one
// turn run in parallel, so they take Lock first.
public sealed class PlanStream
{
    private readonly IExecutionEngine _engine;
    private readonly ThreadRecorder _recorder;
    private readonly ClientProgress _progress;
    private readonly IReadOnlyList<AiNode> _executors;
    private readonly WorkspaceScope _scope;
    private readonly RuntimeSettings _settings;
    private readonly FileJournal _journal;
    private readonly Func<ThreadPlan, CancellationToken, Task>? _beforeRun;
    private readonly CancellationToken _cancellationToken;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<string, bool> _existence = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _folders = [];
    // Tasks that have their ThreadTask and went (or go) to the engine.
    private readonly HashSet<string> _dispatched = new(StringComparer.OrdinalIgnoreCase);
    private readonly ExecutionFeed _feed;
    private Task _dispatch = Task.CompletedTask;
    private Task<ExecutionSummary>? _run;
    private FeedBatch? _final;
    private int _nextOrder;

    // beforeRun: what must happen before a batch's files change (the unit test baseline of their tests).
    // earlier/earlierResults: the work of the run before the planner paused, and the results of its executed tasks.
    public PlanStream(IExecutionEngine engine, ThreadRecorder recorder, ClientProgress progress, IReadOnlyList<AiNode> executors, WorkspaceScope scope,
        RuntimeSettings settings, FileJournal journal, bool executeEarly, Func<ThreadPlan, CancellationToken, Task>? beforeRun, CancellationToken cancellationToken,
        CommittedWork? earlier = null, IReadOnlyList<TaskResult>? earlierResults = null)
    {
        _engine = engine;
        _recorder = recorder;
        _progress = progress;
        _executors = executors;
        _scope = scope;
        _settings = settings;
        _journal = journal;
        _beforeRun = beforeRun;
        _cancellationToken = cancellationToken;
        ExecutesEarly = executeEarly && executors.Count > 0;

        var seeded = new List<TaskResult>();
        if (earlier is not null)
        {
            Committed = Clone(earlier.Plan);
            foreach (var (path, existed) in earlier.Existence) _existence[path] = existed;
            _folders.AddRange(earlier.Folders);
            Batches = earlier.Batches;
            if (earlier.Executed)
            {
                foreach (var task in Committed.Tasks) _dispatched.Add(task.Id);
                seeded.AddRange((earlierResults ?? []).Where(r => _dispatched.Contains(r.TaskKey)));
                scope.AllowWrites(_folders);
            }
        }
        _feed = new ExecutionFeed(seeded);
        _nextOrder = (earlierResults?.Count ?? 0) + 1;
    }

    public bool ExecutesEarly { get; }

    // The committed part of the plan: summary, contract and tasks (no test, tests or publish section).
    public ThreadPlan Committed { get; private set; } = new();

    public int Batches { get; private set; }

    public bool HasCommitted => Committed.Tasks.Count > 0;

    // Tasks that already run (or ran) on the engine.
    public int Executed => _dispatched.Count;

    public IReadOnlyDictionary<string, bool> Existence => _existence;

    // Folders outside the project root the committed tasks change; the developer allowed them when they were committed.
    public IReadOnlyList<string> Folders => _folders;

    public async Task<IDisposable> LockAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        return new Releaser(_lock);
    }

    // The committed plan with the submitted tasks and contract items added, for the validator (with Existence and the
    // committed ids as earlier tasks). The submitted summary wins when it has one; the test, tests, publish and answer
    // sections are the submitted ones. Errors: problems the planner can fix (ids already used).
    public ThreadPlan Merge(ThreadPlan submitted, out List<string> errors)
    {
        errors = [];
        var committedIds = Committed.Tasks.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var reused = submitted.Tasks.Select(t => t.Id?.Trim() ?? string.Empty).Where(committedIds.Contains).ToList();
        if (reused.Count > 0)
            errors.Add($"Task ids {string.Join(", ", reused)} were already committed with commit_tasks. Send only the tasks that are not committed yet, with new ids (the next free one is T{NextNumber()}). To change a committed file, add a new task for it.");

        foreach (var task in submitted.Tasks)
        {
            task.After = null;
            Record(task.File);
            Record(task.To);
        }

        var merged = Clone(Committed);
        merged.Summary = string.IsNullOrWhiteSpace(submitted.Summary) ? Committed.Summary : submitted.Summary;
        merged.Answer = submitted.Answer;
        merged.Test = submitted.Test;
        merged.Tests = submitted.Tests;
        merged.Publish = submitted.Publish;
        foreach (var item in submitted.Contract.Where(c => !string.IsNullOrWhiteSpace(c.Name)))
            if (!merged.Contract.Any(c => c.Name == item.Name && c.Value == item.Value))
                merged.Contract.Add(item);
        merged.Tasks.AddRange(submitted.Tasks);
        return merged;
    }

    public IReadOnlySet<string> CommittedIds => Committed.Tasks.Select(t => t.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    // The tasks of a validated merged plan that are not committed yet wait for the earlier tasks on their files.
    public void Order(ThreadPlan merged)
    {
        var committed = merged.Tasks.Where(t => CommittedIds.Contains(t.Id)).ToList();
        foreach (var task in merged.Tasks.Where(t => !CommittedIds.Contains(t.Id)))
        {
            var paths = Paths(task);
            var after = committed.Where(c => Paths(c).Overlaps(paths) && !(task.DependsOn ?? []).Contains(c.Id, StringComparer.OrdinalIgnoreCase))
                .Select(c => c.Id).ToList();
            task.After = after.Count > 0 ? after : null;
        }
    }

    // Takes a validated merged plan (Merge, then the validator, then Order) as the committed one; with early execution
    // its new tasks start right away. Returns the ids of the new tasks.
    public async Task<IReadOnlyList<PlanTask>> CommitAsync(ThreadPlan merged, CancellationToken cancellationToken)
    {
        var added = merged.Tasks.Where(t => !CommittedIds.Contains(t.Id)).ToList();
        Committed = new ThreadPlan { Summary = merged.Summary, Contract = merged.Contract.ToList(), Tasks = merged.Tasks.ToList() };
        foreach (var folder in merged.OutsideFolders ?? [])
            if (!_folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) _folders.Add(folder);
        Batches++;
        if (!ExecutesEarly || added.Count == 0) return added;

        _scope.AllowWrites(_folders);
        var entities = await _recorder.CreateTasksAsync(new ThreadPlan { Tasks = added }, _nextOrder);
        _nextOrder += added.Count;
        foreach (var task in added) _dispatched.Add(task.Id);
        // The executors of this batch see the contract as committed so far.
        var batch = new FeedBatch(new ThreadPlan { Summary = Committed.Summary, Contract = Committed.Contract.ToList() }, added, entities);
        _dispatch = DispatchAsync(_dispatch, batch);
        _run ??= StartRun(new ThreadPlan());
        await _recorder.UpdateThreadAsync(t => t.PlanJson = Committed.ToJson());
        return added;
    }

    // How the committed tasks ended since the last call (early execution only).
    public IReadOnlyList<TaskResult> TakeFinished() => _feed.TakeFinished();

    public CommittedWork Snapshot() =>
        new(Clone(Committed), new Dictionary<string, bool>(_existence, StringComparer.OrdinalIgnoreCase), _folders.ToList(), Batches, _dispatched.Count > 0);

    // The final plan (committed tasks first): the tasks that have not gone to the engine get their ThreadTask, and what
    // must happen before files change (beforeRun) is done for the whole plan. Then RunAsync.
    public async Task PrepareFinalAsync(ThreadPlan plan, CancellationToken cancellationToken)
    {
        var rest = plan.Tasks.Where(t => !_dispatched.Contains(t.Id)).ToList();
        var entities = rest.Count > 0 ? await _recorder.CreateTasksAsync(new ThreadPlan { Tasks = rest }, _nextOrder) : [];
        _nextOrder += rest.Count;
        await _dispatch;
        if (_beforeRun is not null) await _beforeRun(plan, cancellationToken);
        _final = new FeedBatch(plan, rest, entities);
    }

    // Runs the rest of the plan and waits for every task, the committed ones included.
    public async Task<ExecutionSummary> RunAsync()
    {
        if (_final is { Tasks.Count: > 0 } final)
        {
            _scope.AllowWrites(_folders);
            _feed.Add(final);
        }
        _feed.Complete();
        _run ??= StartRun(_final?.Plan ?? new ThreadPlan());
        return await _run;
    }

    // The planner stopped (a question, a permission, a failure): the tasks already sent to the engine finish first, so
    // no file changes while the thread waits or after it ended. Nothing more can be committed to this stream.
    public async Task DrainAsync()
    {
        try
        {
            await _dispatch;
        }
        catch (Exception)
        {
            // DispatchAsync logs its own problems.
        }
        _feed.Complete();
        if (_run is null) return;
        try
        {
            await _run;
        }
        catch (Exception)
        {
            // Cancelled with the thread, or failed: the tasks record their own state.
        }
    }

    // What the planner reads before its next turn: how its committed tasks went.
    public static string ResultsNote(IReadOnlyList<TaskResult> results)
    {
        var lines = results.Select(r => r.Status switch
        {
            ThreadTaskStatus.Succeeded => $"- {r.TaskKey} ({r.File}) succeeded: {Clip(r.Summary)}",
            ThreadTaskStatus.Skipped => $"- {r.TaskKey} ({r.File}) was skipped: {Clip(r.Error)}",
            _ => $"- {r.TaskKey} ({r.File}) failed: {Clip(r.Error)}"
        });
        var trouble = results.Any(r => r.Status != ThreadTaskStatus.Succeeded)
            ? "\nFor a failed or skipped task, commit a new task for the same file with a corrected instruction (it runs after the earlier one), or leave it to AIT's verification at the end (build and review with fix rounds)."
            : string.Empty;
        return "# Committed task results\n" + string.Join("\n", lines) + trouble;

        static string Clip(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "-";
            var line = text.ReplaceLineEndings(" ").Trim();
            return line.Length > 300 ? line[..300] + "…" : line;
        }
    }

    private async Task DispatchAsync(Task previous, FeedBatch batch)
    {
        try
        {
            await previous;
        }
        catch (Exception)
        {
            // Logged by the earlier dispatch.
        }

        if (_beforeRun is not null)
        {
            try
            {
                await _beforeRun(new ThreadPlan { Summary = batch.Plan.Summary, Tasks = batch.Tasks.ToList() }, _cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _recorder.Log(ActivityLevel.Warning, Lang.T($"Baseline unit test untuk batch rencana gagal: {ex.Message}", $"The unit test baseline of a plan batch failed: {ex.Message}"));
            }
        }
        _feed.Add(batch);
    }

    private Task<ExecutionSummary> StartRun(ThreadPlan plan) =>
        _engine.RunAsync(new ExecutionRequest(plan, [], _executors, _scope, _settings, _journal, Feed: _feed), _recorder, _progress, _cancellationToken);

    // Whether the file existed before the first task that may change it was committed; kept from then on.
    private void Record(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var normalized = path.Trim().Replace('\\', '/');
        if (!_scope.TryResolve(normalized, out var full, out _) && !_scope.TryResolveOutside(normalized, out full, out _, out _)) return;
        if (!_existence.ContainsKey(full)) _existence[full] = File.Exists(full);
    }

    private int NextNumber() =>
        Committed.Tasks.Select(t => t.Id.Length > 1 && (t.Id[0] is 'T' or 't') && int.TryParse(t.Id[1..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;

    private static HashSet<string> Paths(PlanTask task)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { task.File };
        if (task.To is not null) paths.Add(task.To);
        return paths;
    }

    private static ThreadPlan Clone(ThreadPlan plan) =>
        ThreadPlan.TryParse(plan.ToJson(), out var copy, out var error) && copy is not null ? copy : throw new InvalidOperationException(error);

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
