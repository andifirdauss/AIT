using AIThread.Common.Enums;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    public async Task CompleteBaselineAsync(UnitTestSession session, ThreadPlan plan, IReadOnlyCollection<string> modules, ThreadRecorder recorder,
        ClientProgress progress, CancellationToken cancellationToken)
    {
        if (!session.CanRun) return;
        if (session.BaselineRun is { } running) await running;

        // The plan names the files: the tests related to them, the tests it updates, never the ones it retires.
        var files = plan.Tasks.SelectMany(t => t.To is null ? [t.File] : new[] { t.File, t.To }).ToList();
        var related = UnitTestSelection.Relevant(session.Tests, session.Settings.Mode, files, modules).ToList();
        var decisions = plan.Tests?.Decisions ?? [];
        foreach (var decision in decisions.Where(d => d.Decision == PlanTests.Update))
            if (session.ByNumber(decision.Test) is { } updated && !related.Contains(updated)) related.Add(updated);
        foreach (var test in related) session.Relevant.Add(test.Id);
        var retired = decisions.Where(d => d.Decision == PlanTests.Obsolete).Select(d => d.Test).ToHashSet(StringComparer.OrdinalIgnoreCase);
        session.Relevant.RemoveWhere(id => session.Tests.FirstOrDefault(t => t.Id == id) is { } test && retired.Contains(test.DisplayNumber));
        if (session.Relevant.Count == 0 && (plan.Tests?.New.Count ?? 0) == 0) return;

        await progress.PhaseAsync("🧪", $"Unit test · baseline · {session.Relevant.Count} test terkait", cancellationToken);
        var pending = session.Tests.Where(t => session.Relevant.Contains(t.Id) && !session.Baseline.ContainsKey(t.Id)).ToList();
        if (pending.Count > 0) await BaselineAsync(session, pending, recorder, cancellationToken);

        session.Report.BaselineCached = session.Cached.Count;
        if (session.Relevant.Count == 0)
        {
            await progress.StepAsync("belum ada test terkait; test baru dari rencana dijalankan setelah build", cancellationToken, last: true);
            return;
        }
        var verdicts = session.Relevant.Select(id => session.Baseline.GetValueOrDefault(id)).OfType<TestVerdict>().ToList();
        var parts = new List<string> { $"{session.Report.BaselineRan} dijalankan, {session.Report.BaselineCached} dari hasil sebelumnya" };
        parts.Add($"{verdicts.Count(v => v.Result == UnitTestResult.Passed)} lolos");
        if (verdicts.Count(v => v.IsFailure) is > 0 and var failing) parts.Add($"{failing} sudah gagal sebelum perubahan");
        if (verdicts.Count(v => v.Result is UnitTestResult.Skipped or UnitTestResult.NotFound) is > 0 and var skipped) parts.Add($"{skipped} dilewati");
        var seconds = session.Report.BaselineRan > 0 ? $" ({session.Report.BaselineMs / 1000.0:0.0}s)" : string.Empty;
        await progress.StepAsync(string.Join(" · ", parts) + seconds, cancellationToken, last: true);
    }
}
