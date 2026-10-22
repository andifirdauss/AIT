using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // The tester's verdict per failing test decides what gets fixed: the code for a regression, the test only when it
    // was written or updated in this change (or the planner kept a test the request deliberately changes). Without a
    // tester, or for a test it gave no usable verdict, the code under test is fixed. Returns the tester's tokens.
    private async Task<int> TriageAsync(UnitTestSession session, UnitTestCheckInput input, IReadOnlyList<(UnitTestCase Test, UnitTestReportItem Item, string Label)> problems,
        UnitTestRoundReport round, List<ReviewIssue> issues, List<string> unfixable, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        UnitTestTriageResult? triage = null;
        if (input.Tester is { } tester)
        {
            await progress.StepAsync($"🩺 Triase · {problems.Count} test gagal · {tester.Model}", cancellationToken, hang: 3);
            var failures = problems.Select(p => new UnitTestFailure(p.Label, p.Test.Title, p.Test.Purpose ?? p.Test.Scenario, p.Item.Decision ?? PlanTests.Keep,
                p.Test.TestFile, Source(session, p.Test), p.Item.Message)).ToList();
            triage = await testerAgent.TriageAsync(new UnitTestTriageInput(tester, input.Request, input.Plan, input.Tasks, input.Results, failures),
                recorder, progress, cancellationToken);
            round.TesterModel = triage.Node?.Model ?? tester.Model;
            session.Report.TesterModel = round.TesterModel;
            if (triage.Error is not null)
            {
                await progress.DetailAsync($"🟡 Tester tidak memberi hasil: {triage.Error} — perbaikan diarahkan ke kode yang diuji.", cancellationToken);
                recorder.Log(ActivityLevel.Warning, $"Triase unit test gagal: {triage.Error}");
            }
            round.Triage = triage.Summary;
        }

        foreach (var (test, item, label) in problems)
        {
            var isNew = item.Decision == "new";
            var verdict = triage?.Verdicts.FirstOrDefault(v => string.Equals(v.Test, label, StringComparison.OrdinalIgnoreCase));
            // A test written in this change is a dry-run test: one that reaches a database, the network or a config file
            // while it runs is not isolated, so it is fixed, not skipped as "the environment" (that would let the thread
            // pass with no test that ran). A restore or build that cannot run here (a NuGet feed that needs credentials,
            // a missing SDK) is no fault of the test: that stays a skip.
            if (isNew && verdict?.Verdict == UnitTestTriageVerdict.Environment && !string.IsNullOrWhiteSpace(test.TestFile)
                && TestEnvironment.Classify(item.Message) is not null)
            {
                verdict = verdict with
                {
                    Verdict = UnitTestTriageVerdict.TestWrong,
                    File = test.TestFile,
                    Anchor = null,
                    Fix = $"The test is not isolated: it reaches {verdict.Reason ?? "a database, the network or a config file"}. " +
                          "Make it a dry-run test: fake or inject every database, network call, config and clock it touches (add the smallest refactor that lets the test pass a fake in), and keep the same scenario and assertions."
                };
            }
            item.Triage = verdict?.Verdict;
            item.TriageReason = verdict?.Reason;
            switch (verdict?.Verdict)
            {
                case UnitTestTriageVerdict.Environment:
                    item.Verdict = UnitTestReportItem.SkippedVerdict;
                    item.SkipReason = $"Lingkungan (menurut Tester): {verdict.Reason ?? "kebutuhan test tidak ada di mesin ini"}";
                    session.After[test.Id] = session.After[test.Id] with { Result = UnitTestResult.Skipped, SkipReason = item.SkipReason };
                    break;
                case UnitTestTriageVerdict.Flaky:
                    item.Verdict = UnitTestReportItem.Flaky;
                    session.Quarantine[test.Id] = verdict.Reason ?? "Tester menilai test ini tidak stabil (gagal tanpa sebab di kode).";
                    break;
                case UnitTestTriageVerdict.TestWrong when isNew || item.Decision == PlanTests.Update:
                case UnitTestTriageVerdict.PlanWrong:
                    if (verdict.Verdict == UnitTestTriageVerdict.PlanWrong)
                    {
                        item.Decision = PlanTests.Update;
                        session.Report.Notes.Add($"{item.Number} diperbarui mengikuti perubahan perilaku yang diminta (menurut Tester): {verdict.Reason}");
                    }
                    issues.Add(new ReviewIssue(test.TestFile, verdict.Anchor, Problem(test, label, item, isNew),
                        verdict.Fix ?? $"Update the test to the behaviour the request asks for: {verdict.Reason}"));
                    break;
                case UnitTestTriageVerdict.Regression when verdict.File is { } file && (!IsTestFile(session, file) || isNew || item.Decision == PlanTests.Update):
                    issues.Add(new ReviewIssue(file, verdict.Anchor, Problem(test, label, item, isNew), verdict.Fix ?? CodeFix(test, isNew)));
                    break;
                default:
                    // No tester, no usable verdict, or a test of another developer the tester wanted changed.
                    if (verdict?.Verdict == UnitTestTriageVerdict.TestWrong)
                        session.Report.Notes.Add($"Tester menilai {item.Number} salah, tetapi test lama hanya boleh diubah lewat keputusan update; yang diperbaiki kodenya.");
                    MechanicalIssues(session, input, test, label, item, isNew, issues, unfixable);
                    break;
            }
        }
        return triage?.Tokens ?? 0;
    }
}
