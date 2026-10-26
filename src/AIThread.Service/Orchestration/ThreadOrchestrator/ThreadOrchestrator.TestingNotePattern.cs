using System.Text.RegularExpressions;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The note of either language (TestingNote), so a note written before the language changed is replaced too.
    [GeneratedRegex(@"\s*Testing: \d+ (?:skenario gagal|scenario\(s\) failed)\.")]
    private static partial Regex TestingNotePattern();
}
