using System.Diagnostics;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Auto Testing: after verification the tester node checks the change on the running app the way the plan's test
    // section says. Null when it does not run (off, no Tester node, nothing changed). testOnly: nothing was meant to
    // change; the tester checks the existing behaviour. A crash in here only costs the test.
    private async Task<TestingResult?> TestAsync(
        Pipeline pipeline,
        ThreadPlan plan,
        ExecutionSummary execution,
        VerificationResult? verification,
        CancellationToken cancellationToken,
        bool testOnly = false)
    {
        if (pipeline.Tester is not { } tester || plan.Test is not { } test) return null;
        if (!testOnly && !execution.Results.Any(r => r.Status == ThreadTaskStatus.Succeeded)) return null;

        var (recorder, progress) = (pipeline.Recorder, pipeline.Progress);
        var lastBuild = verification?.Report.Rounds.LastOrDefault(r => r.Build is not null)?.Build;
        var skip = !test.IsRunnable ? test.Notes ?? Lang.T("planner menilai perubahan ini tidak bisa dites dari luar", "the planner judged this change cannot be tested from outside")
            : lastBuild is { Status: "failed" } ? Lang.T("build masih gagal, jadi aplikasi belum bisa memuat perubahan ini", "the build still fails, so the application cannot load this change yet")
            : null;
        if (skip is not null)
        {
            var skipped = new TestingReport { Outcome = TestingReport.Skipped, Level = test.Level, BaseUrl = test.BaseUrl, Summary = skip };
            await progress.PhaseAsync("🧪", Lang.T("Testing · dilewati", "Testing · skipped"), cancellationToken);
            await progress.StepAsync(skip, cancellationToken, last: true);
            recorder.Log(ActivityLevel.Info, Lang.T($"Testing dilewati: {skip}", $"Testing skipped: {skip}"));
            await recorder.UpdateThreadAsync(t => t.TestingJson = skipped.ToJson());
            return new TestingResult(skipped, 0, TesterAgent.ForModels(skipped), tester);
        }

        await recorder.UpdateThreadAsync(t =>
        {
            t.Status = ThreadStatus.Testing;
            t.TesterModel = tester.Model;
        });
        // A login the session gave earlier is used without asking; only a chat can be asked for one.
        var request = pipeline.Request;
        var login = testLogins.Recall(request.SessionId, pipeline.Project.ProjectId);
        var canAskLogin = request.Source == ThreadSource.ClaudeCode && !string.IsNullOrEmpty(request.SessionId);
        await progress.PhaseAsync("🧪", $"Testing · {(test.Level == TestPlan.Ui ? "UI" : "API")} · {test.BaseUrl} · {tester.Model}" +
                                       (login is null ? string.Empty : $" · login {login.Username}"), cancellationToken);

        var clock = Stopwatch.StartNew();
        try
        {
            var result = AskForLogin(await testerAgent.RunAsync(new TestingInput(tester, request, plan, execution, pipeline.Project, login, canAskLogin),
                recorder, progress, cancellationToken), request.SessionId, pipeline.Project.ProjectId, recorder.Thread.Id);
            clock.Stop();
            await recorder.UpdateThreadAsync(t =>
            {
                t.TestingMs = (t.TestingMs ?? 0) + clock.ElapsedMilliseconds;
                t.TestingJson = result.Report.ToJson();
                t.TesterTokens += result.Tokens;
                t.TesterModel = result.Node.Model;
            });
            var closing = TestingClosing(result.Report);
            recorder.Log(result.Report.Outcome == TestingReport.Passed ? ActivityLevel.Success : ActivityLevel.Warning, $"Testing: {closing}.");
            await progress.StepAsync(closing, cancellationToken, last: true);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Testing of thread {ThreadId} crashed", recorder.Thread.Id);
            recorder.Log(ActivityLevel.Error, Lang.T($"Testing gagal: {ex.Message}", $"Testing failed: {ex.Message}"));
            await progress.StepAsync(Lang.T($"🟡 Testing gagal: {ex.Message}", $"🟡 Testing failed: {ex.Message}"), cancellationToken, hang: 3);
            await progress.StepAsync(Lang.T("testing dihentikan", "testing stopped"), cancellationToken, last: true);
            await recorder.UpdateThreadAsync(t => t.TestingMs = (t.TestingMs ?? 0) + clock.ElapsedMilliseconds);
            return null;
        }
    }
}
