namespace AIThread.Service.CliBackend;

// How long one CLI call may take in total, and how long it may stay silent (no output line) before it is dropped.
public sealed record CliCallLimits(TimeSpan Timeout, TimeSpan Stall);

// What one CLI run is asked: the system prompt, the user turn (a whole transcript when there was a conversation) and
// the images of that turn.
public sealed record CliPrompt(string System, string User, IReadOnlyList<CliImage> Images);

public sealed record CliImage(string MediaType, string Data);

// What one CLI run answered. Text is the model's reply; the token counts are 0 when the CLI did not report them.
public sealed record CliRunOutput(
    string Text,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int CachedTokens);
