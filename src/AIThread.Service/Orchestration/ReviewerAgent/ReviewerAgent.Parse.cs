using System.Text.Json.Nodes;

namespace AIThread.Service.Orchestration;

public sealed partial class ReviewerAgent
{
    public static ReviewResult Parse(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return ReviewResult.Failed("Reviewer did not answer with JSON.");

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(content[start..(end + 1)]);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return ReviewResult.Failed($"Reviewer answered with invalid JSON ({ex.Message}).");
        }

        var issues = new List<ReviewIssue>();
        foreach (var item in root?["issues"] as JsonArray ?? [])
        {
            var file = Text(item?["file"])?.Trim().Trim('`');
            var problem = Text(item?["problem"])?.Trim();
            if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(problem)) continue;

            var fix = Text(item?["fix"])?.Trim();
            var anchor = Text(item?["anchor"])?.Trim();
            var create = item?["create"] is JsonValue flag && flag.TryGetValue<bool>(out var value) && value;
            issues.Add(new ReviewIssue(file, string.IsNullOrWhiteSpace(anchor) ? null : anchor, problem, string.IsNullOrWhiteSpace(fix) ? problem : fix, create));
            if (issues.Count >= MaxIssues) break;
        }

        // The issue list decides: "approved": false without an issue gives an executor nothing to do.
        return new ReviewResult(issues.Count == 0, Text(root?["summary"])?.Trim(), issues, 0);
    }
}
