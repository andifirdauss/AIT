using System.Text;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    public async Task<(string ForModel, string Headline)> RunForPlannerAsync(UnitTestSession session, IReadOnlyList<string> numbers, string? file, bool all, CancellationToken cancellationToken)
    {
        if (!session.CanRun)
            return ("Error: no test runner of this project can run on this machine: " + string.Join("; ", session.Report.Problems), Lang.T("🟡 tidak ada runner test yang bisa jalan", "🟡 no test runner that can run"));

        var normalized = file?.Trim().Replace('\\', '/').TrimStart('.', '/');
        var selected = all ? session.Active.ToList()
            : normalized is not null ? session.Active.Where(t => string.Equals(t.TestFile, normalized, StringComparison.OrdinalIgnoreCase)).ToList()
            : numbers.Select(n => UnitTestText.ParseNumber(n) is { } number ? session.ByNumber(number) : null).OfType<UnitTestCase>().Distinct().ToList();
        if (selected.Count == 0)
            return ("Error: no active test of the catalog matches. Give numbers from \"# Unit tests\" (e.g. \"UT-0012\"), a registered test file, or all.",
                Lang.T("🟡 tidak ada test katalog yang cocok", "🟡 no matching catalog test"));

        var execution = await executor.RunAsync(session.Project.RootPath, session.Suites, selected, session.Timeout, retryFailures: false, cancellationToken);
        // Nothing has changed yet: these results are the baseline too.
        foreach (var (id, verdict) in execution.Verdicts) session.Baseline.TryAdd(id, verdict);

        var builder = new StringBuilder();
        var passed = selected.Count(t => execution.Verdicts[t.Id].Result == UnitTestResult.Passed);
        var failed = selected.Count(t => execution.Verdicts[t.Id].IsFailure);
        builder.Append($"Ran {selected.Count} test(s) with {string.Join(", ", execution.Suites.Select(s => $"`{s.Display}`"))}: {passed} passed, {failed} failed, {selected.Count - passed - failed} skipped.\n");
        foreach (var test in selected)
        {
            var verdict = execution.Verdicts[test.Id];
            builder.Append($"- {test.DisplayNumber} \"{test.Title}\" ({test.TestFile}, {test.MethodName}): {verdict.Result.ToString().ToUpperInvariant()}");
            if (verdict.SkipReason is not null) builder.Append($" — {verdict.SkipReason}");
            builder.Append('\n');
            if (verdict.IsFailure && verdict.Message is not null) builder.Append("```\n").Append(UnitTestText.Clip(verdict.Message, MaxMessageChars * 2)).Append("\n```\n");
        }
        var skipped = selected.Count - passed - failed;
        var headline = (failed > 0 ? "🔴" : "✅") + Lang.T($" {selected.Count} test · {passed} lolos", $" {selected.Count} tests · {passed} passed") + (failed > 0 ? Lang.T($", {failed} gagal", $", {failed} failed") : string.Empty)
                       + (skipped > 0 ? Lang.T($", {skipped} dilewati", $", {skipped} skipped") : string.Empty) + $" ({execution.DurationMs / 1000.0:0.0}s)";
        return (builder.ToString(), headline);
    }
}
