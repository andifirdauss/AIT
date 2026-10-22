using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.UnitTests;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    public async Task<UnitTestCheck> CheckAsync(UnitTestSession session, UnitTestCheckInput input, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var round = new UnitTestRoundReport { Round = input.Round };
        session.Report.Rounds.Add(round);
        var plan = input.Plan;
        var retired = (plan.Tests?.Decisions ?? []).Where(d => d.Decision == PlanTests.Obsolete).Select(d => d.Test).ToHashSet(StringComparer.OrdinalIgnoreCase);

        FindNewTests(session, plan, input.ThreadId);
        // Fix tasks may touch files the plan did not: the tests of those files join.
        foreach (var test in UnitTestSelection.Relevant(session.Tests, UnitTestMode.Relevant, input.ChangedFiles, []))
            if (!retired.Contains(test.DisplayNumber)) session.Relevant.Add(test.Id);
        var tests = session.Tests.Where(t => session.Relevant.Contains(t.Id) && t.State == UnitTestState.Active).Concat(session.NewTests).ToList();
        if (!session.CanRun)
            return new UnitTestCheck(round, [], [], 0, 0,
                $"Unit tests: not run — no test runner of this project can run on this machine: {string.Join("; ", session.Report.Problems)}\n");
        if (tests.Count == 0)
            return new UnitTestCheck(round, [], [], 0, 0,
                $"Unit tests: AIT checked the project's catalog ({session.Active.Count()} active tests); none is related to the changed files, so none had to run.\n");

        var verdicts = new Dictionary<Guid, TestVerdict>();
        var toRun = new List<UnitTestCase>();
        foreach (var test in tests)
        {
            if (EnvironmentSkip(session, test) is { } skip) verdicts[test.Id] = skip;
            else toRun.Add(test);
        }
        if (toRun.Count > 0)
        {
            var execution = await executor.RunAsync(session.Project.RootPath, session.Suites, toRun, session.Timeout, retryFailures: true, cancellationToken);
            foreach (var (id, verdict) in execution.Verdicts) verdicts[id] = verdict;
            round.DurationMs = execution.DurationMs;
            round.Commands = execution.Suites.Select(s => s.Display).ToList();
        }
        round.Ran = toRun.Count;

        var problems = new List<(UnitTestCase Test, UnitTestReportItem Item, string Label)>();
        var newIndex = 0;
        foreach (var test in tests)
        {
            var isNew = session.NewTests.Contains(test);
            if (isNew) newIndex++;
            var decision = isNew ? "new" : plan.Tests?.DecisionFor(test.DisplayNumber)?.Decision ?? PlanTests.Keep;
            var verdict = verdicts[test.Id];
            session.After[test.Id] = verdict;
            var baseline = session.Baseline.GetValueOrDefault(test.Id);

            var item = Item(session, test, isNew, decision);
            item.Baseline = baseline is null ? null : Lower(baseline.Result);
            item.BaselineCached = session.Cached.ContainsKey(test.Id);
            item.After = Lower(verdict.Result);
            item.Message = verdict.Message is null ? null : UnitTestText.Clip(verdict.Message, MaxMessageChars);
            item.SkipReason = verdict.SkipReason;
            item.DurationMs = verdict.DurationMs;
            item.Triage = null;
            item.TriageReason = null;
            item.Verdict = Classify(test, baseline, verdict, isNew, decision);
            if (item.IsProblem) problems.Add((test, item, isNew ? $"NEW-{newIndex}" : test.DisplayNumber));
        }

        round.Passed = tests.Count(t => verdicts[t.Id].Result == UnitTestResult.Passed);
        round.Failed = tests.Count(t => verdicts[t.Id].IsFailure);
        round.Skipped = tests.Count - round.Passed - round.Failed;
        round.Preexisting = tests.Where(t => session.Items[t.Id].Verdict == UnitTestReportItem.Preexisting).Select(t => session.Items[t.Id].Number).ToList();

        var issues = new List<ReviewIssue>();
        var unfixable = new List<string>();
        var testerTokens = 0;
        if (problems.Count > 0)
            testerTokens = await TriageAsync(session, input, problems, round, issues, unfixable, recorder, progress, cancellationToken);

        round.Regressions = tests.Select(t => session.Items[t.Id]).Where(i => i.IsProblem).Select(i => i.Number).ToList();
        round.FixTargets = issues.Select(i => i.File).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        await ReportRoundAsync(session, tests, round, recorder, progress, cancellationToken);
        return new UnitTestCheck(round, issues, unfixable, round.Regressions.Count, testerTokens, ForModels(session, tests, round));
    }
}
