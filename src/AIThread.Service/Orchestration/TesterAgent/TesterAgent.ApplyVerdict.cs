using System.Text.Json.Nodes;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    private const string NoVerdict = "Tester tidak memberi hasil untuk skenario ini.";
    private const string NeedsLogin = "Butuh login.";

    // The verdicts of report_test, matched to the planned scenarios by title, else by position. Scenarios without one
    // stay blocked. arguments null: the tester stopped without report_test; its text, if any, becomes the summary.
    public static void ApplyVerdict(TestingReport report, string? arguments, string? text = null)
    {
        var args = arguments is null ? null : PlannerToolRunner.ParseArguments(arguments, out _);
        report.Summary = (args is null ? null : PlannerToolRunner.Str(args, "summary"))?.Trim()
                         ?? (string.IsNullOrWhiteSpace(text) ? null : PlannerToolRunner.Limit(text.Trim(), 1_000));

        var verdicts = (args?["scenarios"] as JsonArray)?.OfType<JsonObject>().ToList() ?? [];
        var unmatched = new List<JsonObject>();
        foreach (var verdict in verdicts)
        {
            var title = PlannerToolRunner.Str(verdict, "title")?.Trim();
            var scenario = title is null ? null : report.Scenarios.FirstOrDefault(s => s.Details is null && string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase));
            if (scenario is null) unmatched.Add(verdict);
            else Apply(scenario, verdict);
        }
        foreach (var verdict in unmatched)
        {
            var open = report.Scenarios.FirstOrDefault(s => s.Details is null);
            if (open is null)
            {
                open = new TestScenarioResult { Title = PlannerToolRunner.Str(verdict, "title")?.Trim() ?? $"Skenario {report.Scenarios.Count + 1}" };
                report.Scenarios.Add(open);
            }
            Apply(open, verdict);
        }
        foreach (var scenario in report.Scenarios.Where(s => s.Details is null))
        {
            scenario.Status = TestingReport.Blocked;
            scenario.Details = report.Error ?? (report.LoginNeeded is null ? NoVerdict : NeedsLogin);
        }
    }

    private static void Apply(TestScenarioResult scenario, JsonObject verdict)
    {
        var status = PlannerToolRunner.Str(verdict, "status")?.Trim().ToLowerInvariant();
        scenario.Status = status is TestingReport.Passed or TestingReport.Failed ? status : TestingReport.Blocked;
        scenario.Details = PlannerToolRunner.Str(verdict, "details")?.Trim() ?? string.Empty;
    }
}
