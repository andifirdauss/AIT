using AIThread.Common.Entities;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // Any failed scenario fails the test; otherwise one passed scenario passes it (blocked ones are listed).
    private static TestingResult Done(TestRun run, int tokens, AiNode node)
    {
        var report = run.Report;
        report.Outcome = report.Count(TestingReport.Failed) > 0 ? TestingReport.Failed
            : report.Count(TestingReport.Passed) > 0 ? TestingReport.Passed
            : TestingReport.Blocked;
        report.TesterModel = node.Model;
        if (run.Browser is { } browser) report.BrowserErrors = browser.Problems.Select(run.Mask).ToList();
        return new TestingResult(report, tokens, ForModels(report), node);
    }
}
