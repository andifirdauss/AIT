using System.Text;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Verification;

namespace AIThread.Service.Orchestration;

public sealed partial class VerificationStage
{
    public async Task<VerificationResult> RunAsync(VerificationInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var report = new VerificationReport { Command = input.Build?.Display, ReviewerModel = input.Reviewer?.Model };
        var tasks = input.Plan.Tasks.ToList();
        var results = input.Execution.Results.ToList();
        var forModels = new StringBuilder();
        var order = input.NextTaskOrder;
        var buildCommand = input.Build;
        int executorTokens = 0, reviewerTokens = 0, testerTokens = 0, testFixRounds = 0;
        long fixWallMs = 0, fixSumMs = 0;

        for (var fixRound = 1; ; fixRound++)
        {
            var round = input.FirstRound + fixRound - 1;
            var current = new VerificationRound { Round = round };
            report.Rounds.Add(current);
            forModels.Append($"\n## Round {round}\n");

            BuildResult? build = null;
            if (buildCommand is not null)
            {
                build = await buildRunner.RunAsync(buildCommand, TimeSpan.FromSeconds(input.Settings.BuildTimeoutSeconds), cancellationToken);
                current.Build = ToReport(build, input.Scope);

                var icon = build.Passed ? "✅" : build.TimedOut || build.StartError is not null ? "🟡" : "🔨";
                await progress.StepAsync($"{icon} {Capitalize(BuildReport.Headline(build))}", cancellationToken, hang: 3);
                foreach (var error in build.Errors.Where(e => !e.IsLockedOutput).Take(MaxErrorsInProgress))
                    await progress.DetailAsync(BuildReport.Line(error, input.Scope), cancellationToken);
                recorder.Log(build.Passed ? ActivityLevel.Success : ActivityLevel.Warning, Lang.T($"Verifikasi putaran {round}: {BuildReport.Headline(build)}.", $"Verification round {round}: {BuildReport.Headline(build)}."));
                forModels.Append(BuildReport.Format(build, input.Scope));
            }

            ReviewResult? review = null;
            if (input.Reviewer is { } reviewer)
            {
                review = await ReviewAsync(reviewer, input, tasks, results, build, recorder, progress, cancellationToken);
                reviewerTokens += review.Tokens;
                current.Review = new ReviewRoundReport { Approved = review.Approved, Summary = review.Summary, Issues = review.Issues.ToList(), Error = review.Error };
                await ReportReviewAsync(review, round, recorder, progress, cancellationToken);
                forModels.Append(ReviewText(review));
            }

            var buildUsable = build is { TimedOut: false, StartError: null };
            var reviewUsable = review is { Error: null };

            // A move or an extract also changes its destination.
            var byId = tasks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            var changedFiles = results.Where(r => r.Status == ThreadTaskStatus.Succeeded)
                .SelectMany(r => byId.TryGetValue(r.TaskKey, out var t) && t.To is not null ? [r.File, t.To] : new[] { r.File })
                .ToList();

            // Unit tests run on a build that passes (a project without a build step AIT can run counts as passing).
            UnitTestCheck? unit = null;
            if (input.UnitTests is { } session && (build is null || build.Passed))
            {
                unit = await unitTestStage.CheckAsync(session,
                    new UnitTestCheckInput(input.Request, input.Plan, tasks, results, changedFiles, round, input.Tester, recorder.Thread.Id),
                    recorder, progress, cancellationToken);
                current.UnitTests = unit.Round;
                testerTokens += unit.TesterTokens;
                forModels.Append(unit.ForModels);
            }

            // A build that timed out or cannot start would do the same in the next round.
            if (build is not null && !buildUsable) buildCommand = null;
            var buildReviewProblems = (buildUsable && !build!.Passed) || (reviewUsable && review!.Issues.Count > 0);
            var testProblems = unit is { Issues.Count: > 0 };
            if (!buildReviewProblems && !testProblems)
            {
                current.Unfixable = unit?.Unfixable.ToList() ?? [];
                report.Outcome = unit is { Problems: > 0 } ? VerificationReport.Failed
                    : buildUsable || reviewUsable || unit is not null ? VerificationReport.Passed
                    : VerificationReport.Unverified;
                if (current.Unfixable.Count > 0)
                    forModels.Append("Not fixable automatically:\n").Append(string.Concat(current.Unfixable.Select(u => $"- {u}\n")));
                break;
            }

            var canFixBuildReview = buildReviewProblems && fixRound <= input.Settings.MaxFixRounds;
            var canFixTests = testProblems && input.Settings.MaxFixRounds > 0 && testFixRounds < UnitTestStage.MaxFixRounds;
            var fix = FixTaskFactory.Create(round, canFixBuildReview && buildUsable ? build : null, canFixBuildReview && reviewUsable ? review : null, changedFiles, tasks,
                input.Scope, canFixTests ? unit!.Issues : null);
            current.Unfixable = fix.Unfixable.Concat(unit?.Unfixable ?? []).ToList();
            if (current.Unfixable.Count > 0)
                forModels.Append("Not fixable automatically:\n").Append(string.Concat(current.Unfixable.Select(u => $"- {u}\n")));

            report.Outcome = VerificationReport.Failed;
            if (!canFixBuildReview && !canFixTests)
            {
                var note = input.Settings.MaxFixRounds == 0
                    ? Lang.T("Perbaikan otomatis dimatikan (putaran perbaikan = 0); masalah dilaporkan apa adanya.", "Automatic fixes are off (fix rounds = 0); issues are reported as they are.")
                    : buildReviewProblems
                        ? Lang.T($"Masih ada masalah setelah {input.Settings.MaxFixRounds} putaran perbaikan.", $"Issues remain after {input.Settings.MaxFixRounds} fix round(s).")
                        : Lang.T($"Masih ada regresi unit test setelah {UnitTestStage.MaxFixRounds} putaran perbaikan.", $"Unit test regressions remain after {UnitTestStage.MaxFixRounds} fix round(s).");
                await progress.StepAsync($"🟡 {note}", cancellationToken, hang: 3);
                recorder.Log(ActivityLevel.Warning, Lang.T($"Verifikasi: {note}", $"Verification: {note}"));
                break;
            }
            if (fix.Tasks.Count == 0)
            {
                await progress.StepAsync(Lang.T("🟡 Masalah yang tersisa tidak bisa diperbaiki otomatis (lihat ringkasan).", "🟡 The remaining issues cannot be fixed automatically (see the summary)."), cancellationToken, hang: 3);
                recorder.Log(ActivityLevel.Warning, Lang.T($"Verifikasi: {current.Unfixable.Count} masalah tidak bisa diperbaiki otomatis.", $"Verification: {current.Unfixable.Count} issue(s) cannot be fixed automatically."));
                break;
            }
            if (canFixTests) testFixRounds++;

            await progress.StepAsync(Lang.T($"🔧 Perbaikan {round} · {fix.Tasks.Count} tugas", $"🔧 Fix {round} · {fix.Tasks.Count} task(s)"), cancellationToken, hang: 3);
            recorder.Log(ActivityLevel.Info, Lang.T($"Verifikasi putaran {round}: {fix.Tasks.Count} tugas perbaikan.", $"Verification round {round}: {fix.Tasks.Count} fix task(s)."));

            var fixPlan = new ThreadPlan { Summary = input.Plan.Summary, Contract = input.Plan.Contract, Tasks = fix.Tasks.ToList() };
            var entities = await recorder.CreateTasksAsync(fixPlan, order);
            order += fix.Tasks.Count;

            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Executing);
            var execution = await executionEngine.RunAsync(new ExecutionRequest(fixPlan, entities, input.Executors, input.Scope, input.Settings, input.Journal, Nested: true), recorder, progress, cancellationToken);
            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Verifying);

            executorTokens += execution.Tokens;
            fixWallMs += execution.WallMs;
            fixSumMs += execution.SumMs;
            tasks.AddRange(fix.Tasks);
            results.AddRange(execution.Results);
            current.FixTasks = fix.Tasks.Select(t => t.Id).ToList();
            forModels.Append($"Fix tasks: {string.Join(", ", execution.Results.Select(r => $"{r.TaskKey} {r.File} [{r.Status}]"))}\n");

            if (!execution.Results.Any(r => r.Status == ThreadTaskStatus.Succeeded))
            {
                await progress.StepAsync(Lang.T("🟡 Tidak ada tugas perbaikan yang berhasil; verifikasi dihentikan.", "🟡 No fix task succeeded; verification stopped."), cancellationToken, hang: 3);
                break;
            }
        }

        forModels.Insert(0, $"Outcome: {report.Outcome}" +
                            (input.Build is null ? " (no build command; reviewer only)" : $" — build command `{input.Build.Display}`") +
                            (input.Reviewer is null ? ", no reviewer node" : string.Empty) +
                            (input.UnitTests is null ? string.Empty : ", with the project's unit tests") + "\n");

        var plan = new ThreadPlan { Summary = input.Plan.Summary, Answer = input.Plan.Answer, Contract = input.Plan.Contract, Tasks = tasks, Test = input.Plan.Test, Tests = input.Plan.Tests };
        var summary = new ExecutionSummary(results, input.Execution.WallMs + fixWallMs, input.Execution.SumMs + fixSumMs, input.Execution.Tokens + executorTokens);
        return new VerificationResult(report, plan, summary, executorTokens, reviewerTokens, forModels.ToString(), testerTokens);
    }
}
