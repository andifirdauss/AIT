using System.Text.RegularExpressions;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    [GeneratedRegex(@"\s*Testing: \d+ skenario gagal\.")]
    private static partial Regex TestingNotePattern();
}
