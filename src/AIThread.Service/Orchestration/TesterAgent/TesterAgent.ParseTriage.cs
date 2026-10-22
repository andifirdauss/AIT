using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // Only verdicts for the failures it was given; an unknown verdict counts as regression (the code gets fixed).
    public static UnitTestTriageResult ParseTriage(string content, IReadOnlyList<UnitTestFailure> failures)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return new UnitTestTriageResult([], null, 0, null!, "Tester did not answer with JSON.");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content[start..(end + 1)]);
        }
        catch (JsonException ex)
        {
            return new UnitTestTriageResult([], null, 0, null!, $"Tester answered with invalid JSON ({ex.Message}).");
        }

        var known = failures.Select(f => f.Test).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var verdicts = new List<UnitTestTriageVerdict>();
        foreach (var item in root?["verdicts"] as JsonArray ?? [])
        {
            var test = Text(item?["test"])?.Trim();
            if (test is null || !known.Contains(test) || verdicts.Any(v => string.Equals(v.Test, test, StringComparison.OrdinalIgnoreCase))) continue;
            var verdict = Text(item?["verdict"])?.Trim().ToLowerInvariant() switch
            {
                UnitTestTriageVerdict.TestWrong => UnitTestTriageVerdict.TestWrong,
                UnitTestTriageVerdict.PlanWrong => UnitTestTriageVerdict.PlanWrong,
                UnitTestTriageVerdict.Environment => UnitTestTriageVerdict.Environment,
                UnitTestTriageVerdict.Flaky => UnitTestTriageVerdict.Flaky,
                _ => UnitTestTriageVerdict.Regression
            };
            verdicts.Add(new UnitTestTriageVerdict(failures.First(f => string.Equals(f.Test, test, StringComparison.OrdinalIgnoreCase)).Test, verdict,
                Blank(Text(item?["file"])?.Trim().Trim('`')), Blank(Text(item?["anchor"])?.Trim()), Blank(Text(item?["fix"])?.Trim()), Blank(Text(item?["reason"])?.Trim())));
        }
        return new UnitTestTriageResult(verdicts, Blank(Text(root?["summary"])?.Trim()), 0, null!);
    }
}
