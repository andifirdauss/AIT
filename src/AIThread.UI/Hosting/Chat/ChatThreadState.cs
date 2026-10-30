using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.UI.Hosting.Chat;

// One change of a followed thread, as the plain output prints it line by line.
public abstract record ThreadChange
{
    public sealed record Status(ThreadStatus Value, string? PlanSummary) : ThreadChange;
    public sealed record Tool(string Name, string Detail) : ThreadChange;
    public sealed record TaskChanged(ThreadTaskDto Value) : ThreadChange;
    public sealed record Output(ThreadTaskDto? Task, string Text) : ThreadChange;
    public sealed record Git(string State) : ThreadChange;
    public sealed record Log(ActivityLevel Level, string Message) : ThreadChange;
    public sealed record ApprovalAsked : ThreadChange;
    public sealed record ApprovalAnswered(Guid? NextThreadId) : ThreadChange;
}

// The stages of a thread as the chat shows them, in the order a thread goes through them.
public enum ChatPhase
{
    Planning,
    Executing,
    Verifying,
    Testing,
    Wrapping
}

// Waiting: not reached yet. Stopped: the thread ended in this stage (failed, cancelled, waiting for the developer).
public enum PhaseState
{
    Waiting,
    Running,
    Done,
    Stopped
}

public sealed record PhaseInfo(ChatPhase Phase, PhaseState State, long? Ms);

// What aithread chat knows about the thread it follows, built from the SSE events and GET /api/threads/{id}.
public sealed class ChatThreadState(Guid threadId)
{
    private const int ActivityLines = 5;
    private const int LogLines = 3;
    private const int OutputChars = 4000;

    private readonly Dictionary<Guid, ThreadTaskDto> _tasks = [];
    private readonly Dictionary<Guid, StringBuilder> _output = [];
    private readonly List<string> _activity = [];
    private readonly Dictionary<ChatPhase, (TimeSpan Start, TimeSpan? End)> _phases = [];
    private readonly Dictionary<ChatPhase, List<(ActivityLevel Level, string Message)>> _logs = [];
    private ChatPhase? _current;
    private bool _known;

    public Guid ThreadId { get; } = threadId;
    public ThreadStatus Status { get; private set; } = ThreadStatus.Pending;
    public string? PlanSummary { get; private set; }
    public string? GitState { get; private set; }
    public Stopwatch Clock { get; } = Stopwatch.StartNew();

    // The last detail read: models, the time of every stage once it ended, the check reports.
    public ThreadDetailDto? Detail { get; private set; }

    // Planner tools seen since following started.
    public int ToolCount { get; private set; }

    // Shown under the progress until the next key (Ctrl+C asks to press again, ...).
    public string? Notice { get; set; }

    // A status other than the one the thread had when following started was seen (a retry restarts a final thread).
    public bool Moved { get; private set; }

    public IReadOnlyList<string> Activity => _activity;

    public IReadOnlyList<ThreadTaskDto> Tasks => _tasks.Values.OrderBy(t => t.Order).ThenBy(t => t.TaskKey, StringComparer.Ordinal).ToList();

    public string OutputOf(Guid taskId) => _output.TryGetValue(taskId, out var text) ? text.ToString() : string.Empty;

    public bool IsFinal => IsFinalStatus(Status);

    public static bool IsFinalStatus(ThreadStatus status) =>
        status is ThreadStatus.Completed or ThreadStatus.Failed or ThreadStatus.Cancelled or ThreadStatus.AwaitingInput or ThreadStatus.RolledBack;

    // The thread's log lines of a stage, newest last (only the last few are kept).
    public IReadOnlyList<(ActivityLevel Level, string Message)> LogsOf(ChatPhase phase) => _logs.TryGetValue(phase, out var lines) ? lines : [];

    public static ChatPhase? PhaseOf(ThreadStatus status) => status switch
    {
        ThreadStatus.Planning => ChatPhase.Planning,
        ThreadStatus.Executing => ChatPhase.Executing,
        ThreadStatus.Verifying => ChatPhase.Verifying,
        ThreadStatus.Testing => ChatPhase.Testing,
        ThreadStatus.Wrapping => ChatPhase.Wrapping,
        _ => null
    };

    // The stages to show: the ones reached (with their time: the thread's own once known, else measured here), the
    // running one, and while the thread runs the ones still ahead. A stage the thread passed without entering is left out.
    public IReadOnlyList<PhaseInfo> Phases()
    {
        var reached = Enum.GetValues<ChatPhase>().Where(p => _phases.ContainsKey(p) || ThreadMs(p) is not null).ToList();
        var last = reached.Count > 0 ? reached.Max() : (ChatPhase?)null;
        var result = new List<PhaseInfo>();
        foreach (var phase in Enum.GetValues<ChatPhase>())
        {
            if (!IsFinal && phase == _current)
                result.Add(new PhaseInfo(phase, PhaseState.Running, (long)(Clock.Elapsed - _phases[phase].Start).TotalMilliseconds));
            else if (reached.Contains(phase))
            {
                // The thread ended in this stage: cancelled, waiting, or failed before its summary. A thread that failed
                // but was summed up ended in the summary normally; the stage that failed shows it (ChatRenderer).
                var stopped = IsFinal && phase == last && Status != ThreadStatus.Completed && (Status != ThreadStatus.Failed || phase != ChatPhase.Wrapping);
                var measured = _phases.TryGetValue(phase, out var span) && span.End is { } end ? (long)(end - span.Start).TotalMilliseconds : (long?)null;
                result.Add(new PhaseInfo(phase, stopped ? PhaseState.Stopped : PhaseState.Done, ThreadMs(phase) ?? measured));
            }
            else if (!IsFinal && (_current is null || phase > _current))
                result.Add(new PhaseInfo(phase, PhaseState.Waiting, null));
        }
        return result;
    }

    private long? ThreadMs(ChatPhase phase) => Detail is not { } detail ? null : phase switch
    {
        ChatPhase.Planning => detail.PlanningMs,
        ChatPhase.Executing => detail.Summary.ExecutionWallMs,
        ChatPhase.Verifying => detail.VerificationMs,
        ChatPhase.Testing => detail.TestingMs,
        ChatPhase.Wrapping => detail.WrappingMs,
        _ => null
    };

    public ThreadChange? Apply(ThreadDetailDto detail)
    {
        // The detail holds every task of the thread: tasks it no longer has (a retry planned again) go.
        Detail = detail;
        _tasks.Clear();
        foreach (var task in detail.Tasks) _tasks[task.Id] = task;
        return SetStatus(detail.Summary.Status, detail.Summary.PlanSummary);
    }

    public ThreadChange? Apply(AppEvent evt)
    {
        if (evt.ThreadId is { } id && id != ThreadId) return null;
        if (evt.Data is not JsonElement data || data.ValueKind != JsonValueKind.Object)
            return evt.Type == AppEventTypes.ApprovalRequested ? new ThreadChange.ApprovalAsked() : null;

        switch (evt.Type)
        {
            case AppEventTypes.ThreadCreated:
            case AppEventTypes.ThreadUpdated:
                return Text(data, "status") is { } raw && ParseStatus(raw) is { } status ? SetStatus(status, Text(data, "planSummary")) : null;

            case AppEventTypes.ThreadOutput when Text(data, "kind") == "planner.tool":
                var tool = Text(data, "tool") ?? "tool";
                var detail = (Text(data, "detail") ?? string.Empty).ReplaceLineEndings(" ");
                _activity.Add($"{tool} · {detail}".TrimEnd(' ', '·'));
                if (_activity.Count > ActivityLines) _activity.RemoveAt(0);
                ToolCount++;
                return new ThreadChange.Tool(tool, detail);

            case AppEventTypes.Log:
                if (Text(data, "message") is not { Length: > 0 } message) return null;
                var level = Enum.TryParse<ActivityLevel>(Text(data, "level"), ignoreCase: true, out var parsed) ? parsed : ActivityLevel.Info;
                // A line logged outside a stage (queued, or after the thread ended: "Thread finished ...") belongs to
                // none; the summary says it already.
                if (_current is { } stage)
                {
                    var lines = _logs.TryGetValue(stage, out var existingLines) ? existingLines : _logs[stage] = [];
                    lines.Add((level, message.ReplaceLineEndings(" ")));
                    if (lines.Count > LogLines) lines.RemoveAt(0);
                }
                return new ThreadChange.Log(level, message);

            case AppEventTypes.TaskUpdated:
                var task = data.Deserialize<ThreadTaskDto>(ChatApiClient.Json);
                if (task is null) return null;
                var changed = !_tasks.TryGetValue(task.Id, out var before) || before.Status != task.Status;
                _tasks[task.Id] = task;
                return changed ? new ThreadChange.TaskChanged(task) : null;

            case AppEventTypes.TaskOutput:
                if (Text(data, "taskId") is not { } rawId || !Guid.TryParse(rawId, out var taskId)) return null;
                var chunk = Text(data, "text") ?? string.Empty;
                var buffer = _output.TryGetValue(taskId, out var existing) ? existing : _output[taskId] = new StringBuilder();
                if (data.TryGetProperty("reset", out var reset) && reset.ValueKind == JsonValueKind.True) buffer.Clear();
                buffer.Append(chunk);
                if (buffer.Length > OutputChars) buffer.Remove(0, buffer.Length - OutputChars);
                return new ThreadChange.Output(_tasks.GetValueOrDefault(taskId), chunk);

            case AppEventTypes.ThreadGit:
                GitState = Text(data, "state");
                return GitState is null ? null : new ThreadChange.Git(GitState);

            case AppEventTypes.ApprovalRequested:
                return new ThreadChange.ApprovalAsked();

            case AppEventTypes.ApprovalChanged:
                return new ThreadChange.ApprovalAnswered(Text(data, "nextThreadId") is { } next && Guid.TryParse(next, out var nextId) ? nextId : null);
        }

        return null;
    }

    private ThreadChange? SetStatus(ThreadStatus status, string? planSummary)
    {
        var summaryChanged = !string.IsNullOrWhiteSpace(planSummary) && planSummary != PlanSummary;
        if (summaryChanged) PlanSummary = planSummary;
        if (status == Status && _known && !summaryChanged) return null;

        // The first status known is where following started, not a move.
        if (status != Status && _known) Moved = true;
        _known = true;
        Status = status;
        EnterPhase(PhaseOf(status));
        return new ThreadChange.Status(status, summaryChanged ? planSummary : null);
    }

    // Ends the running stage and starts the next one (none: the thread waits in the queue or ended). A stage entered
    // again (a retry) keeps its first start.
    private void EnterPhase(ChatPhase? phase)
    {
        if (phase == _current) return;
        var now = Clock.Elapsed;
        if (_current is { } running && _phases.TryGetValue(running, out var span) && span.End is null) _phases[running] = span with { End = now };
        if (phase is { } next)
            _phases[next] = _phases.TryGetValue(next, out var again) ? again with { End = null } : (now, null);
        _current = phase;
    }

    // Enums arrive by name; a number is accepted too.
    public static ThreadStatus? ParseStatus(string raw) =>
        Enum.TryParse<ThreadStatus>(raw, ignoreCase: true, out var status) ? status : null;

    // A property by name in any casing, as text (numbers and booleans included).
    public static string? Text(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => property.Value.GetRawText()
                };
        return null;
    }
}
