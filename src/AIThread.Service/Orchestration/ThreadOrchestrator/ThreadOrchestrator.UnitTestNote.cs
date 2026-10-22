namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The thread's note (ThreadRun.Error) about unit tests that still fail.
    private static string UnitTestNote(UnitTestReport report)
    {
        var problems = report.Tests.Where(t => t.IsProblem || t.Verdict == UnitTestReportItem.Failing).Select(t => t.Number).ToList();
        return report.TestOnly
            ? $"Unit test: {problems.Count} gagal ({string.Join(", ", problems)})."
            : $"Unit test: {problems.Count} masih gagal setelah perubahan ({string.Join(", ", problems)}).";
    }
}
