using System.Text;
using System.Text.Json;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using AiThread.Service.Verification;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed record VerificationInput(
    ThreadRequest Request,
    ThreadPlan Plan,
    ExecutionSummary Execution,
    BuildCommand? Build,
    AiNode? Reviewer,
    IReadOnlyList<AiNode> Executors,
    WorkspaceScope Scope,
    RuntimeSettings Settings,
    int NextTaskOrder);

// Plan and Execution include the fix tasks, ready for the wrapper and memory extraction. ForModels describes the
// rounds in English for the wrapper.
public sealed record VerificationResult(
    VerificationReport Report,
    ThreadPlan Plan,
    ExecutionSummary Execution,
    int ExecutorTokens,
    int ReviewerTokens,
    string ForModels);

// Stored on the thread as JSON (ThreadRun.VerificationJson) for the thread page.
public sealed class VerificationReport
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Unverified = "unverified";

    public string Outcome { get; set; } = Unverified;
    public string? Command { get; set; }
    public string? ReviewerModel { get; set; }
    public List<VerificationRound> Rounds { get; set; } = [];

    public string ToJson() => JsonSerializer.Serialize(this, ThreadPlan.JsonOptions);
}

public sealed class VerificationRound
{
    public int Round { get; set; }
    public BuildRoundReport? Build { get; set; }
    public ReviewRoundReport? Review { get; set; }
    public List<string> FixTasks { get; set; } = [];
    public List<string> Unfixable { get; set; } = [];
}

public sealed class BuildRoundReport
{
    // passed | failed | locked (compiled, output files locked) | timeout | error (could not start)
    public string Status { get; set; } = string.Empty;
    public string Headline { get; set; } = string.Empty;
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public long DurationMs { get; set; }
    public List<string> Errors { get; set; } = [];
    public string? Output { get; set; }
}

public sealed class ReviewRoundReport
{
    public bool Approved { get; set; }
    public string? Summary { get; set; }
    public List<ReviewIssue> Issues { get; set; } = [];
    public string? Error { get; set; }
}

// After the executors: build the project and/or let the reviewer node check the changes, then hand the problems to
// executors as fix tasks and check again, for at most MaxFixRounds rounds. Never fails the thread: problems that
// remain are reported to the developer.
public sealed class VerificationStage(BuildRunner buildRunner, ReviewerAgent reviewerAgent, ExecutionEngine executionEngine)
{
    private const int MaxErrorsInReport = 30;
    private const int MaxErrorsInProgress = 5;
    private const int MaxOutputChars = 3000;

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

        for (var round = 1; ; round++)
        {
            var current = new VerificationRound { Round = round };
            report.Rounds.Add(current);
            forModels.Append($"\n## Round {round}\n");

            BuildResult? build = null;
            if (buildCommand is not null)
            {
                await progress.LineAsync($"🔨 Build `{buildCommand.Display}`…", cancellationToken);
                build = await buildRunner.RunAsync(buildCommand, TimeSpan.FromSeconds(input.Settings.BuildTimeoutSeconds), cancellationToken);
                current.Build = ToReport(build, input.Scope);

                await progress.LineAsync($"   {(build.Passed ? "✅" : "❌")} {BuildReport.Headline(build)}", cancellationToken);
                foreach (var error in build.Errors.Where(e => !e.IsLockedOutput).Take(MaxErrorsInProgress))
                    await progress.LineAsync($"      · {BuildReport.Line(error, input.Scope)}", cancellationToken);
                recorder.Log(build.Passed ? ActivityLevel.Success : ActivityLevel.Warning, $"Verifikasi putaran {round}: {BuildReport.Headline(build)}.");
                forModels.Append(BuildReport.Format(build, input.Scope));
            }

            ReviewResult? review = null;
            if (input.Reviewer is { } reviewer)
            {
                await progress.LineAsync($"🔎 Reviewer `{reviewer.Model}` memeriksa hasil…", cancellationToken);
                review = await ReviewAsync(reviewer, input, tasks, results, build, cancellationToken);
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

            var changedFiles = results.Where(r => r.Status == ThreadTaskStatus.Succeeded).Select(r => r.File).ToList();
            var fix = FixTaskFactory.Create(round, buildUsable ? build : null, reviewUsable ? review : null, changedFiles, tasks, input.Scope);
            current.Unfixable = fix.Unfixable.ToList();
            if (fix.Unfixable.Count > 0)
                forModels.Append("Not fixable automatically:\n").Append(string.Concat(fix.Unfixable.Select(u => $"- {u}\n")));

            report.Outcome = VerificationReport.Failed;
            if (round > input.Settings.MaxFixRounds)
            {
                var note = input.Settings.MaxFixRounds == 0
                    ? "Perbaikan otomatis dimatikan (putaran perbaikan = 0); masalah dilaporkan apa adanya."
                    : $"Masih ada masalah setelah {input.Settings.MaxFixRounds} putaran perbaikan.";
                await progress.LineAsync($"⚠️ {note}", cancellationToken);
                recorder.Log(ActivityLevel.Warning, $"Verifikasi: {note}");
                break;
            }
            if (fix.Tasks.Count == 0)
            {
                await progress.LineAsync("⚠️ Masalah yang tersisa tidak bisa diperbaiki otomatis (lihat ringkasan).", cancellationToken);
                recorder.Log(ActivityLevel.Warning, $"Verifikasi: {fix.Unfixable.Count} masalah tidak bisa diperbaiki otomatis.");
                break;
            }

            await progress.LineAsync($"🛠️ Putaran perbaikan {round}: {fix.Tasks.Count} tugas…", cancellationToken);
            foreach (var task in fix.Tasks) await progress.LineAsync($"   • {task.Id} `{task.File}` — {task.Title}", cancellationToken);
            recorder.Log(ActivityLevel.Info, $"Verifikasi putaran {round}: {fix.Tasks.Count} tugas perbaikan.");

            var fixPlan = new ThreadPlan { Summary = input.Plan.Summary, Contract = input.Plan.Contract, Tasks = fix.Tasks.ToList() };
            var entities = await recorder.CreateTasksAsync(fixPlan, order);
            order += fix.Tasks.Count;

            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Executing);
            var execution = await executionEngine.RunAsync(new ExecutionRequest(fixPlan, entities, input.Executors, input.Scope, input.Settings), recorder, progress, cancellationToken);
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
                await progress.LineAsync("⚠️ Tidak ada tugas perbaikan yang berhasil; verifikasi dihentikan.", cancellationToken);
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

    // A reviewer that cannot be reached or answers unreadably only costs the review, never the thread.
    private async Task<ReviewResult> ReviewAsync(AiNode reviewer, VerificationInput input, IReadOnlyList<PlanTask> tasks, IReadOnlyList<TaskResult> results, BuildResult? build, CancellationToken cancellationToken)
    {
        try
        {
            var buildText = build is null ? null : BuildReport.Format(build, input.Scope);
            return await reviewerAgent.ReviewAsync(reviewer, input.Request.Prompt, input.Plan, tasks, results, buildText, cancellationToken);
        }
        catch (NineRouterException ex)
        {
            return ReviewResult.Failed(ex.Message);
        }
    }

    private static async Task ReportReviewAsync(ReviewResult review, int round, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (review.Error is not null)
        {
            await progress.LineAsync($"   ⚠️ Reviewer tidak memberi hasil: {review.Error}", cancellationToken);
            recorder.Log(ActivityLevel.Warning, $"Reviewer putaran {round} gagal: {review.Error}");
            return;
        }

        if (review.Issues.Count == 0)
        {
            await progress.LineAsync($"   ✅ Reviewer: {review.Summary ?? "tidak ada masalah."}", cancellationToken);
            recorder.Log(ActivityLevel.Success, $"Reviewer putaran {round}: disetujui. {review.Summary}");
            return;
        }

        await progress.LineAsync($"   ⚠️ Reviewer menemukan {review.Issues.Count} masalah{(review.Summary is null ? "." : $": {review.Summary}")}", cancellationToken);
        foreach (var issue in review.Issues.Take(MaxErrorsInProgress))
            await progress.LineAsync($"      · `{issue.File}` — {issue.Problem}", cancellationToken);
        recorder.Log(ActivityLevel.Warning, $"Reviewer putaran {round}: {review.Issues.Count} masalah. {review.Summary}");
    }

    private static string ReviewText(ReviewResult review)
    {
        if (review.Error is not null) return $"Reviewer: no result ({review.Error}).\n";
        if (review.Issues.Count == 0) return $"Reviewer: approved. {review.Summary}\n";

        var builder = new StringBuilder($"Reviewer: {review.Issues.Count} issue(s). {review.Summary}\n");
        foreach (var issue in review.Issues) builder.Append($"- {issue.File}: {issue.Problem} → {issue.Fix}\n");
        return builder.ToString();
    }

    private static BuildRoundReport ToReport(BuildResult build, WorkspaceScope scope) => new()
    {
        Status = build.StartError is not null ? "error"
            : build.TimedOut ? "timeout"
            : build.Succeeded ? "passed"
            : build.OnlyLockedOutput ? "locked"
            : "failed",
        Headline = BuildReport.Headline(build),
        ErrorCount = build.Errors.Count,
        WarningCount = build.WarningCount,
        DurationMs = build.DurationMs,
        Errors = build.Errors.Take(MaxErrorsInReport).Select(e => BuildReport.Line(e, scope)).ToList(),
        Output = build.Passed || build.Errors.Count > 0 || string.IsNullOrWhiteSpace(build.OutputTail) ? null
            : build.OutputTail.Length <= MaxOutputChars ? build.OutputTail : "…" + build.OutputTail[^MaxOutputChars..]
    };
}
