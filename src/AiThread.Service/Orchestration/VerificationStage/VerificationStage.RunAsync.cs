using System.Text;
using AiThread.Common.Enums;
using AiThread.Service.Verification;

namespace AiThread.Service.Orchestration;

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
        int executorTokens = 0, reviewerTokens = 0;
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
                recorder.Log(build.Passed ? ActivityLevel.Success : ActivityLevel.Warning, $"Verifikasi putaran {round}: {BuildReport.Headline(build)}.");
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

            // A build that timed out or cannot start would do the same in the next round.
            if (build is not null && !buildUsable) buildCommand = null;
            var problems = (buildUsable && !build!.Passed) || (reviewUsable && review!.Issues.Count > 0);
            if (!problems)
            {
                report.Outcome = buildUsable || reviewUsable ? VerificationReport.Passed : VerificationReport.Unverified;
                break;
            }

            // A move or an extract also changes its destination.
            var byId = tasks.GroupBy(t => t.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);
            var changedFiles = results.Where(r => r.Status == ThreadTaskStatus.Succeeded)
                .SelectMany(r => byId.TryGetValue(r.TaskKey, out var t) && t.To is not null ? [r.File, t.To] : new[] { r.File })
                .ToList();
            var fix = FixTaskFactory.Create(round, buildUsable ? build : null, reviewUsable ? review : null, changedFiles, tasks, input.Scope);
            current.Unfixable = fix.Unfixable.ToList();
            if (fix.Unfixable.Count > 0)
                forModels.Append("Not fixable automatically:\n").Append(string.Concat(fix.Unfixable.Select(u => $"- {u}\n")));

            report.Outcome = VerificationReport.Failed;
            if (fixRound > input.Settings.MaxFixRounds)
            {
                var note = input.Settings.MaxFixRounds == 0
                    ? "Perbaikan otomatis dimatikan (putaran perbaikan = 0); masalah dilaporkan apa adanya."
                    : $"Masih ada masalah setelah {input.Settings.MaxFixRounds} putaran perbaikan.";
                await progress.StepAsync($"🟡 {note}", cancellationToken, hang: 3);
                recorder.Log(ActivityLevel.Warning, $"Verifikasi: {note}");
                break;
            }
            if (fix.Tasks.Count == 0)
            {
                await progress.StepAsync("🟡 Masalah yang tersisa tidak bisa diperbaiki otomatis (lihat ringkasan).", cancellationToken, hang: 3);
                recorder.Log(ActivityLevel.Warning, $"Verifikasi: {fix.Unfixable.Count} masalah tidak bisa diperbaiki otomatis.");
                break;
            }

            await progress.StepAsync($"🔧 Perbaikan {round} · {fix.Tasks.Count} tugas", cancellationToken, hang: 3);
            recorder.Log(ActivityLevel.Info, $"Verifikasi putaran {round}: {fix.Tasks.Count} tugas perbaikan.");

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
                await progress.StepAsync("🟡 Tidak ada tugas perbaikan yang berhasil; verifikasi dihentikan.", cancellationToken, hang: 3);
                break;
            }
        }

        forModels.Insert(0, $"Outcome: {report.Outcome}" +
                            (input.Build is null ? " (no build command; reviewer only)" : $" — build command `{input.Build.Display}`") +
                            (input.Reviewer is null ? ", no reviewer node" : string.Empty) + "\n");

        var plan = new ThreadPlan { Summary = input.Plan.Summary, Answer = input.Plan.Answer, Contract = input.Plan.Contract, Tasks = tasks };
        var summary = new ExecutionSummary(results, input.Execution.WallMs + fixWallMs, input.Execution.SumMs + fixSumMs, input.Execution.Tokens + executorTokens);
        return new VerificationResult(report, plan, summary, executorTokens, reviewerTokens, forModels.ToString());
    }
}
