using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // input.Plan.Test is runnable (ui or api) with a local base URL; PlanValidator checked it.
    public async Task<TestingResult> RunAsync(TestingInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var test = input.Plan.Test!;
        var report = new TestingReport
        {
            Level = test.Level,
            BaseUrl = test.BaseUrl,
            TesterModel = input.Tester.Model,
            LoginUser = input.Login?.Username,
            Scenarios = test.Scenarios.Select(s => new TestScenarioResult { Title = s.Title }).ToList()
        };
        using var http = CreateClient();
        using var run = new TestRun(input, report, http, secrets);
        run.Accounts = await AccountsAsync(input.Project, cancellationToken);
        try
        {
            // An app that is not running is started first; one that still does not answer fails every scenario the same
            // way, with no model call for that.
            if (await ProbeAsync(http, test.BaseUrl!, cancellationToken) is { } reason
                && await StartAppAsync(run, reason, recorder, progress, cancellationToken) is { } unreachable)
            {
                report.Error = unreachable;
                foreach (var scenario in report.Scenarios) scenario.Details = unreachable;
                await progress.StepAsync($"🟡 {unreachable}", cancellationToken, hang: 3);
                recorder.Log(ActivityLevel.Warning, $"Testing: {unreachable}");
                return Done(run, 0, input.Tester);
            }
            await EnsureImpersonationAsync(run, recorder, progress, cancellationToken);
            return await LoopAsync(run, recorder, progress, cancellationToken);
        }
        finally
        {
            if (run.Browser is { } browser)
            {
                if (!cancellationToken.IsCancellationRequested) await Task.Delay(Linger, CancellationToken.None);
                await browser.DisposeAsync();
            }
            if (run.App is { } app) await app.DisposeAsync();
        }
    }

    private async Task<TestingResult> LoopAsync(TestRun run, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        var node = run.Input.Tester;
        var tools = TesterTools.For(run.Test.Level, run.Input.CanAskLogin, CanSwitchUser(run));
        var messages = new JsonArray { await SystemMessageAsync(node, cancellationToken), ChatMessages.User(Brief(run.Input, run.Accounts)) };
        var tokens = 0;

        for (var step = 1; ; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var forced = step > MaxSteps;
            if (step == MaxSteps)
                messages.Add(ChatMessages.User("You have reached the step limit. Call report_test now with what you checked so far; mark the scenarios you could not finish \"blocked\"."));

            var current = node;
            var reply = await caller.ChatAsync(current, async candidate =>
            {
                if (candidate.Id != current.Id) messages[0] = await SystemMessageAsync(candidate, cancellationToken);
                return new ChatCompletionRequest
                {
                    Model = candidate.Model,
                    Messages = messages,
                    Tools = forced ? TesterTools.ReportOnly() : tools,
                    Temperature = candidate.Temperature ?? 0.1,
                    MaxTokens = candidate.MaxTokens,
                    PromptCacheKey = ChatCompletionRequest.CacheKey(recorder.Thread.Id, "tester")
                };
            }, cancellationToken, onSwitch: s => s.ReportAsync("Tester", recorder, progress, cancellationToken));
            node = reply.Node;
            var result = reply.Result;
            tokens += result.TotalTokens;
            run.Report.Steps = step;

            if (result.ToolCalls.FirstOrDefault(c => c.Name == TesterTools.ReportTest) is { } verdict)
            {
                ApplyVerdict(run.Report, verdict.Arguments);
                await ObserveImpersonationAsync(verdict.Arguments, run, recorder, cancellationToken);
                return Done(run, tokens, node);
            }
            if (run.Input.CanAskLogin && result.ToolCalls.FirstOrDefault(c => c.Name == TesterTools.AskLogin) is { } ask)
            {
                var args = PlannerToolRunner.ParseArguments(ask.Arguments, out _);
                run.Report.LoginNeeded = (args is null ? null : PlannerToolRunner.Str(args, "reason"))?.Trim() ?? "Aplikasi meminta login.";
                run.Report.LoginUrl = args is null ? null : PlannerToolRunner.Str(args, "loginUrl")?.Trim();
                // The development password can only be refused when the tester logged in with it.
                run.Report.LoginKind = args is not null && PlannerToolRunner.Str(args, "kind")?.Trim() == TestingReport.LoginKindDevPassword && run.Account is not null
                    ? TestingReport.LoginKindDevPassword
                    : TestingReport.LoginKindLogin;
                recorder.Log(ActivityLevel.Info, $"Tester meminta login: {run.Report.LoginNeeded}");
                await progress.StepAsync($"🔐 Butuh login: {run.Report.LoginNeeded}", cancellationToken, hang: 3);
                ApplyVerdict(run.Report, null);
                return Done(run, tokens, node);
            }
            if (result.ToolCalls.Count == 0 || forced)
            {
                // No verdict: what was not reported stays blocked.
                ApplyVerdict(run.Report, null, result.Content);
                return Done(run, tokens, node);
            }

            messages.Add(ChatMessages.Assistant(string.IsNullOrWhiteSpace(result.Content) ? null : result.Content, result.ToolCalls));
            // Browser actions build on each other, so the calls of a turn run in the order written. The turn's token
            // badge goes on its first tool line.
            var badge = ClientProgress.TokenBadge(result.PromptTokens, result.CompletionTokens, result.CachedTokens);
            for (var i = 0; i < result.ToolCalls.Count; i++)
            {
                var call = result.ToolCalls[i];
                var output = await RunToolAsync(call, run, recorder, progress, i == 0 ? badge : null, cancellationToken);
                messages.Add(ChatMessages.Tool(call.Id, output));
            }
        }
    }
}
