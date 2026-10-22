using System.Text.Json.Nodes;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    public const int MaxFollowUpsPerRun = 5;
    private const int MaxFollowUpScenarios = 6;
    private const int MaxFollowUpFields = 12;

    // The follow_ups of report_test, numbered from 1. A suggestion without a scenario that says what to expect is dropped.
    public static void ApplyFollowUps(TestingReport report, string? arguments)
    {
        var args = arguments is null ? null : PlannerToolRunner.ParseArguments(arguments, out _);
        if (args?["follow_ups"] is not JsonArray items) return;
        foreach (var item in items.OfType<JsonObject>().Take(MaxFollowUpsPerRun))
        {
            var title = PlannerToolRunner.Str(item, "title")?.Trim();
            if (string.IsNullOrEmpty(title)) continue;
            var scenarios = (item["scenarios"] as JsonArray)?.OfType<JsonObject>().Take(MaxFollowUpScenarios).Select(Scenario).Where(s => s is not null).ToList() ?? [];
            if (scenarios.Count == 0) continue;
            report.FollowUps.Add(new TestFollowUp
            {
                Number = report.FollowUps.Count == 0 ? 1 : report.FollowUps.Max(f => f.Number) + 1,
                Title = title,
                Why = Field(item, "why"),
                Needs = Field(item, "needs"),
                Scenarios = scenarios!,
                Fields = Memory.MemoryAi.Strings(item["fields"]).Select(f => f.Trim()).Where(f => f.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(MaxFollowUpFields).ToList()
            });
        }
    }

    private static TestScenario? Scenario(JsonObject item)
    {
        var expect = Field(item, "expect");
        if (expect is null) return null;
        return new TestScenario
        {
            Title = Field(item, "title") ?? "Skenario",
            Actor = Field(item, "actor"),
            Action = Field(item, "action"),
            Target = Field(item, "target"),
            Steps = Memory.MemoryAi.Strings(item["steps"]).Select(s => s.Trim()).Where(s => s.Length > 0).ToList(),
            Expect = expect
        };
    }

    private static string? Field(JsonObject item, string name) => PlannerToolRunner.Str(item, name)?.Trim() is { Length: > 0 } value ? value : null;
}
