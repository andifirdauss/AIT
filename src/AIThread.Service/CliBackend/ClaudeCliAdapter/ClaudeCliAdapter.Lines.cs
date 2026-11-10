namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private static Func<string, Task> Lines(ClaudeStreamReader reader, Func<string, Task>? onDelta) =>
        async line =>
        {
            var delta = reader.Feed(line);
            if (delta is not null && onDelta is not null) await onDelta(delta);
        };
}
