using System.Text;
using AIThread.Common.Enums;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // The "# Unit tests" section of the planner's brief: the suites, the rule for new tests, and the registered tests
    // related to the files and modules the request names, with their last result.
    public static string Brief(UnitTestSession session, IReadOnlyCollection<string> files, IReadOnlyCollection<string> modules)
    {
        var builder = new StringBuilder("# Unit tests (the project's catalog: AIT runs the related ones before and after your change)\n");
        foreach (var suite in session.Suites)
            builder.Append($"- Suite: {suite.Framework} · {suite.Manifest}").Append(suite.CanRun ? string.Empty : $" (cannot run on this machine: {suite.Problem})").Append('\n');
        builder.Append(session.Settings.Authoring switch
        {
            UnitTestAuthoring.AutoLogic => "New tests: when the developer asks, and for changes to business logic no test covers yet.\n",
            UnitTestAuthoring.Off => "New tests: turned off for this project.\n",
            _ => "New tests: only when the developer asks for tests.\n"
        });

        var active = session.Active.ToList();
        var related = UnitTestSelection.Relevant(active, session.Settings.Mode == UnitTestMode.All ? UnitTestMode.Relevant : session.Settings.Mode, files, modules);
        if (active.Count == 0) return builder.Append("No active test is registered yet.\n").ToString();
        if (related.Count == 0)
        {
            builder.Append($"None of the {active.Count} active tests is related to the files this request names");
            builder.Append(session.Settings.Mode == UnitTestMode.All ? " (all of them run before and after the change anyway)" : string.Empty);
            // What the catalog does cover, so a request to test this change is not answered with other features' tests.
            var covered = active.GroupBy(t => t.TestFile, StringComparer.OrdinalIgnoreCase).OrderByDescending(g => g.Count()).ToList();
            builder.Append(". The catalog covers only: ")
                .Append(string.Join(", ", covered.Take(MaxBriefTestFiles).Select(g => $"{g.Key} ({g.Count()})")))
                .Append(covered.Count > MaxBriefTestFiles ? $", … {covered.Count - MaxBriefTestFiles} more test files" : string.Empty)
                .Append(".\n");
            return builder.ToString();
        }

        builder.Append("Related tests (number · title — purpose · test file → targets · last result):\n");
        foreach (var test in related.Take(MaxBriefTests))
        {
            builder.Append($"- {test.DisplayNumber} · {test.Title}");
            if (test.Purpose is not null) builder.Append($" — {UnitTestText.Clip(test.Purpose, 200)}");
            builder.Append($" · {test.TestFile} ({test.MethodName})");
            var targets = UnitTestText.Lines(test.TargetFiles);
            if (targets.Count > 0) builder.Append($" → {string.Join(", ", targets)}");
            builder.Append($" · {test.LastResult.ToString().ToLowerInvariant()}").Append('\n');
        }
        if (related.Count > MaxBriefTests) builder.Append($"- … {related.Count - MaxBriefTests} more related tests\n");
        var others = active.Count - related.Count;
        if (others > 0) builder.Append($"{others} other active tests are not related{(session.Settings.Mode == UnitTestMode.All ? " (they run too: mode all)" : string.Empty)}.\n");
        return builder.ToString();
    }
}
