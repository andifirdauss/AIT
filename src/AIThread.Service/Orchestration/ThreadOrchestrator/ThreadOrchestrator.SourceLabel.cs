using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static string SourceLabel(ThreadSource source) => source switch
    {
        ThreadSource.ClaudeCode => "Claude Code",
        ThreadSource.Telegram => "Telegram",
        _ => "dashboard"
    };
}
