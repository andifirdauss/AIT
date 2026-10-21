namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The thread's notes (ThreadRun.Error) with the note of this test instead of an earlier one.
    private static string? WithTestingNote(string? error, TestingReport report)
    {
        var rest = TestingNotePattern().Replace(error ?? string.Empty, string.Empty).Trim();
        var note = report.Outcome == TestingReport.Failed ? TestingNote(report) : null;
        var text = string.Join(" ", new[] { rest, note }.Where(s => !string.IsNullOrEmpty(s)));
        return text.Length == 0 ? null : text;
    }
}
