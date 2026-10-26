using AIThread.Common.Localization;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // "Testing: 2 skenario gagal." — the thread's note for a failed test; TestingNotePattern finds it again.
    private static string TestingNote(TestingReport report) => Lang.T($"Testing: {report.Count(TestingReport.Failed)} skenario gagal.", $"Testing: {report.Count(TestingReport.Failed)} scenario(s) failed.");
}
