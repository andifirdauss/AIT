using AiThread.Common.Enums;

namespace AiThread.Service.Models;

public sealed record ThreadRequest(
    ThreadSource Source,
    // Clean request text (system reminders stripped) — stored, shown in the UI and given to the wrapper.
    string Prompt,
    // Full text of the latest user turn including injected context such as CLAUDE.md; given to the planner.
    string? PromptContext,
    string? ConversationContext,
    string? WorkingDirectory,
    string? SolutionHint,
    Guid? ProjectId,
    string? SessionId);

public sealed record ThreadResult(Guid ThreadId, ThreadStatus Status, string FinalText, int TotalTokens);

// Receives what the client (Claude Code) should see while a thread runs.
public interface IThreadObserver
{
    Task OnProgressAsync(string line, CancellationToken cancellationToken);

    Task OnFinalDeltaAsync(string delta, CancellationToken cancellationToken);
}

public sealed class NullThreadObserver : IThreadObserver
{
    public static readonly NullThreadObserver Instance = new();

    public Task OnProgressAsync(string line, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task OnFinalDeltaAsync(string delta, CancellationToken cancellationToken) => Task.CompletedTask;
}
