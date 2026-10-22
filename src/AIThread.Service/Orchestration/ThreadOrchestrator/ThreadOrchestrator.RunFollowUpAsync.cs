using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private const int MaxFollowUps = 20;

    // One of the tester's suggestions run as a test of the same thread: its scenarios instead of the plan's, the values
    // earlier runs remembered, the project's test accounts. The result is kept on the suggestion, the values it
    // remembered join the thread's, and the suggestions it makes in turn are added (numbered on). The thread's own
    // report stays the main one. Null when the run crashed or was cancelled.
    private async Task<TestingResult?> RunFollowUpAsync(PreparedRetest prepared, int number, ClientProgress progress, bool canAskLogin)
    {
        var (thread, project, plan, execution, tester) = (prepared.Thread, prepared.Project, prepared.Plan, prepared.Execution, prepared.Tester);
        var cancellationToken = prepared.Cancellation.Token;
        var recorder = new ThreadRecorder(dbFactory, eventBus, activityLog, thread);
        var request = new ThreadRequest(thread.Source, thread.Prompt, null, null, thread.WorkingDirectory, project.ProjectId, thread.SessionId);
        var clock = Stopwatch.StartNew();
        try
        {
            var report = TestingReport.FromJson(thread.TestingJson) ?? throw new AppValidationException("Thread ini belum punya hasil testing.");
            var followUp = report.FollowUps.FirstOrDefault(f => f.Number == number)
                           ?? throw new AppValidationException($"Test lanjutan {number} tidak ada di thread ini.");
            var test = plan.Test!;
            var followUpPlan = new ThreadPlan
            {
                Summary = plan.Summary,
                Answer = plan.Answer,
                Contract = plan.Contract,
                Tasks = plan.Tasks,
                Test = new TestPlan
                {
                    Level = test.Level,
                    BaseUrl = test.BaseUrl,
                    Notes = $"Follow-up test {number} of the earlier test of this thread: {followUp.Title}." +
                            (followUp.Why is null ? string.Empty : $" {followUp.Why}") + (test.Notes is null ? string.Empty : $" Earlier notes: {test.Notes}"),
                    Scenarios = followUp.Scenarios
                }
            };

            recorder.Log(ActivityLevel.Info, $"Test lanjutan {number} dijalankan: {followUp.Title}");
            await recorder.UpdateThreadAsync(t =>
            {
                t.Status = ThreadStatus.Testing;
                t.TesterModel = tester.Model;
            });
            await progress.PhaseAsync("🧭", $"Test lanjutan {number} · {followUp.Title} · {tester.Model}", cancellationToken);
            var result = AskForLogin(await testerAgent.RunAsync(new TestingInput(tester, request, followUpPlan, execution, project, null, canAskLogin, report.Values),
                recorder, progress, cancellationToken), thread.SessionId, project.ProjectId, thread.Id);

            var run = result.Report;
            foreach (var (name, value) in run.Values) report.Values[name] = value;
            foreach (var next in run.FollowUps)
            {
                if (report.FollowUps.Count >= MaxFollowUps) break;
                if (report.FollowUps.Any(f => string.Equals(f.Title, next.Title, StringComparison.OrdinalIgnoreCase))) continue;
                next.Number = report.FollowUps.Max(f => f.Number) + 1;
                report.FollowUps.Add(next);
            }
            run.FollowUps = [];
            followUp.LastRun = run;
            followUp.LastRunAt = DateTime.UtcNow;

            await recorder.UpdateThreadAsync(t =>
            {
                t.Status = ThreadStatus.Completed;
                t.TestingMs = (t.TestingMs ?? 0) + clock.ElapsedMilliseconds;
                t.TestingJson = report.ToJson();
                t.TesterTokens += result.Tokens;
                t.TesterModel = result.Node.Model;
            });
            var closing = TestingClosing(run);
            recorder.Log(run.Outcome == TestingReport.Passed ? ActivityLevel.Success : ActivityLevel.Warning, $"Test lanjutan {number}: {closing}.");
            await progress.StepAsync(closing, cancellationToken, last: true);
            // The parent's suggestions (with the new ones) go along, so the answer says what can run next.
            return result with { Report = WithFollowUps(run, report) };
        }
        catch (AppValidationException)
        {
            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Completed);
            throw;
        }
        catch (Exception ex)
        {
            var cancelled = ex is OperationCanceledException;
            if (!cancelled) logger.LogError(ex, "Follow-up test {Number} of thread {ThreadId} crashed", number, thread.Id);
            recorder.Log(cancelled ? ActivityLevel.Warning : ActivityLevel.Error, cancelled ? "Test lanjutan dibatalkan." : $"Test lanjutan gagal: {ex.Message}");
            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Completed);
            return null;
        }
        finally
        {
            registry.Complete(thread.Id);
        }
    }
}
