using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Testing;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The test of a finished thread again, with login when the developer gave one (else the one its session gave). The
    // thread is Completed again afterwards, whatever the test found. canAskLogin: the developer is in the chat, so the
    // tester may ask for a (new) login. Null when the run crashed or was cancelled.
    private async Task<TestingResult?> RetestAsync(PreparedRetest prepared, TestLogin? login, ClientProgress progress, bool canAskLogin)
    {
        var (thread, project, plan, execution, tester) = (prepared.Thread, prepared.Project, prepared.Plan, prepared.Execution, prepared.Tester);
        var test = plan.Test!;
        var cancellationToken = prepared.Cancellation.Token;
        var recorder = new ThreadRecorder(dbFactory, eventBus, activityLog, thread);
        var request = new ThreadRequest(thread.Source, thread.Prompt, null, null, thread.WorkingDirectory, project.ProjectId, thread.SessionId);
        login ??= testLogins.Recall(thread.SessionId, project.ProjectId);
        var clock = Stopwatch.StartNew();
        try
        {
            recorder.Log(ActivityLevel.Info, $"Testing diulang · tester {tester.Model}" + (login is null ? "." : $" · login {login.Username}."));
            await recorder.UpdateThreadAsync(t =>
            {
                t.Status = ThreadStatus.Testing;
                t.TesterModel = tester.Model;
            });
            await progress.PhaseAsync("🧪", $"Testing ulang · {(test.Level == TestPlan.Ui ? "UI" : "API")} · {test.BaseUrl} · {tester.Model}" +
                                           (login is null ? string.Empty : $" · login {login.Username}"), cancellationToken);
            var result = AskForLogin(await testerAgent.RunAsync(new TestingInput(tester, request, plan, execution, project, login, canAskLogin),
                recorder, progress, cancellationToken), thread.SessionId, project.ProjectId, thread.Id);
            await recorder.UpdateThreadAsync(t =>
            {
                t.Status = ThreadStatus.Completed;
                t.TestingMs = (t.TestingMs ?? 0) + clock.ElapsedMilliseconds;
                t.TestingJson = result.Report.ToJson();
                t.TesterTokens += result.Tokens;
                t.TesterModel = result.Node.Model;
                t.Error = WithTestingNote(t.Error, result.Report);
            });
            var closing = TestingClosing(result.Report);
            recorder.Log(result.Report.Outcome == TestingReport.Passed ? ActivityLevel.Success : ActivityLevel.Warning, $"Testing ulang: {closing}.");
            await progress.StepAsync(closing, cancellationToken, last: true);
            return result;
        }
        catch (Exception ex)
        {
            var cancelled = ex is OperationCanceledException;
            if (!cancelled) logger.LogError(ex, "Retest of thread {ThreadId} crashed", thread.Id);
            recorder.Log(cancelled ? ActivityLevel.Warning : ActivityLevel.Error, cancelled ? "Testing ulang dibatalkan." : $"Testing ulang gagal: {ex.Message}");
            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Completed);
            return null;
        }
        finally
        {
            registry.Complete(thread.Id);
        }
    }
}
