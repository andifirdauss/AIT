using AIThread.Common.Enums;
using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static string SourceLabel(ThreadSource source) => source switch
    {
        ThreadSource.ClaudeCode => "CLI",
        ThreadSource.Telegram => "Telegram",
        _ => Lang.T("dasbor", "dashboard")
    };
}
