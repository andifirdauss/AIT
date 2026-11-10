namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    // The CLI takes about a second to shut down after its result line. A failure is left to run out: its stderr says why.
    private static Func<bool> Answered(ClaudeStreamReader reader) => () => reader is { SawResult: true, IsError: false };
}
