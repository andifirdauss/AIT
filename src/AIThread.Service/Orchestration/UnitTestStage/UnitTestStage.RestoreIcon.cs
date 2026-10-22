using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    private static string RestoreIcon(string how) => how switch
    {
        UnitTestRestoreItem.Moved => "↪️",
        UnitTestRestoreItem.Missing => "⚠️",
        UnitTestRestoreItem.Task or UnitTestRestoreItem.Regenerate => "🛠️",
        _ => "♻️"
    };
}
