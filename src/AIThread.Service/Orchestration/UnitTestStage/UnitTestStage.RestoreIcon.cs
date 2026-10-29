using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // Only emoji that are wide by default: a text symbol made emoji by U+FE0F (♻️ ↪️ ⚠️ 🛠️) is drawn two columns wide
    // while the terminal advances one, so it covers the space after it.
    private static string RestoreIcon(string how) => how switch
    {
        UnitTestRestoreItem.Moved => "📂",
        UnitTestRestoreItem.Missing => "❗",
        UnitTestRestoreItem.Task or UnitTestRestoreItem.Regenerate => "🔧",
        _ => "🔄"
    };
}
