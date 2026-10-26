using System.Diagnostics;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Testing;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    private static string StepBlocked => Lang.T("Langkah sebelumnya tidak lolos, jadi langkah ini tidak dijalankan.", "The previous step did not pass, so this step was not run.");

    // A scenario of the thread it came from, one tester session per step (its actor, its form values) so a long flow
    // does not run into the step limit, the remembered values passed from step to step. It stops at the first step that
    // does not pass: the later ones build on it. The combined report goes to the scenario and to the suggestion it came
    // from; the values join the thread's. Null when the run crashed or was cancelled.
    private async Task<TestingResult?> RunScenarioAsync(PreparedRetest prepared, TestingScenario scenario, ClientProgress progress, bool canAskLogin)
    {
        var (thread, project, plan, execution, tester) = (prepared.Thread, prepared.Project, prepared.Plan, prepared.Execution, prepared.Tester);
        var cancellationToken = prepared.Cancellation.Token;
        var recorder = new ThreadRecorder(dbFactory, eventBus, activityLog, thread);
        var request = new ThreadRequest(thread.Source, thread.Prompt, null, null, thread.WorkingDirectory, project.ProjectId, thread.SessionId);
        var steps = ScenarioText.Steps(scenario);
        var roles = ScenarioText.Roles(scenario);
        var clock = Stopwatch.StartNew();
        try
        {
            var report = TestingReport.FromJson(thread.TestingJson) ?? new TestingReport { Level = scenario.Level, BaseUrl = scenario.BaseUrl };
            var values = new Dictionary<string, string>(report.Values, StringComparer.OrdinalIgnoreCase);
            var combined = new TestingReport { Level = scenario.Level, BaseUrl = scenario.BaseUrl ?? plan.Test!.BaseUrl, TesterModel = tester.Model };
            var tokens = 0;
            var node = tester;

            recorder.Log(ActivityLevel.Info, Lang.T($"Skenario test \"{scenario.Title}\" (versi {scenario.Version}, {steps.Count} langkah) dijalankan.", $"Test scenario \"{scenario.Title}\" (version {scenario.Version}, {steps.Count} steps) started."));
            await recorder.UpdateThreadAsync(t =>
            {
                t.Status = ThreadStatus.Testing;
                t.TesterModel = tester.Model;
            });

            for (var i = 0; i < steps.Count; i++)
            {
                var step = steps[i];
                var username = roles.FirstOrDefault(r => string.Equals(r.Role, step.Role, StringComparison.OrdinalIgnoreCase))?.Username;
                var item = ScenarioStep(step, username);
                var stepPlan = new ThreadPlan
                {
                    Summary = plan.Summary, Answer = plan.Answer, Contract = plan.Contract, Tasks = plan.Tasks,
                    Test = new TestPlan
                    {
                        Level = scenario.Level,
                        BaseUrl = combined.BaseUrl,
                        Notes = $"Step {step.Order} of {steps.Count} of the scenario \"{scenario.Title}\" that a developer filled in" +
                                (i == 0 ? "." : "; the earlier steps passed.") +
                                $" Free-text values you type start with \"{ScenarioText.DataMarker}\" so the test data is easy to find." +
                                (scenario.Notes is null ? string.Empty : $" Notes: {scenario.Notes}"),
                        Scenarios = [item]
                    }
                };

                await progress.PhaseAsync("🧩", Lang.T($"Skenario {scenario.Slug} · langkah {step.Order}/{steps.Count} · {step.Action}", $"Scenario {scenario.Slug} · step {step.Order}/{steps.Count} · {step.Action}") +
                                               (username is null ? string.Empty : $" · {username}") + $" · {tester.Model}", cancellationToken);
                var result = AskForLogin(await testerAgent.RunAsync(new TestingInput(tester, request, stepPlan, execution, project, null, canAskLogin, values),
                    recorder, progress, cancellationToken), thread.SessionId, project.ProjectId, thread.Id);
                tokens += result.Tokens;
                node = result.Node;
                var run = result.Report;
                foreach (var (name, value) in run.Values) values[name] = value;

                var outcome = run.Scenarios.FirstOrDefault() ?? new TestScenarioResult { Details = run.Error ?? Lang.T("Tester tidak memberi hasil.", "The Tester gave no result.") };
                outcome.Title = item.Title;
                outcome.Actor ??= username;
                combined.Scenarios.Add(outcome);
                combined.Requests.AddRange(run.Requests.Take(Math.Max(0, 30 - combined.Requests.Count)));
                combined.BrowserErrors.AddRange(run.BrowserErrors);
                foreach (var account in run.Accounts.Where(a => !combined.Accounts.Contains(a, StringComparer.OrdinalIgnoreCase))) combined.Accounts.Add(account);
                combined.DefaultDevPassword |= run.DefaultDevPassword;
                combined.Browser ??= run.Browser;
                combined.AppStarted ??= run.AppStarted;
                combined.Steps += run.Steps;
                if (run.Error is not null) combined.Error ??= run.Error;
                if (run.LoginNeeded is not null)
                {
                    (combined.LoginNeeded, combined.LoginUrl, combined.LoginKind, combined.AskedLogin) = (run.LoginNeeded, run.LoginUrl, run.LoginKind, run.AskedLogin);
                }

                if (outcome.Status == TestingReport.Passed) continue;
                foreach (var later in steps.Skip(i + 1))
                {
                    var laterUser = roles.FirstOrDefault(r => string.Equals(r.Role, later.Role, StringComparison.OrdinalIgnoreCase))?.Username;
                    combined.Scenarios.Add(new TestScenarioResult
                    {
                        Title = ScenarioStep(later, laterUser).Title,
                        Status = TestingReport.Blocked,
                        Details = StepBlocked,
                        Actor = laterUser
                    });
                }
                break;
            }

            combined.Values = values;
            combined.Outcome = combined.Count(TestingReport.Failed) > 0 ? TestingReport.Failed
                : combined.Scenarios.Count > 0 && combined.Count(TestingReport.Passed) == combined.Scenarios.Count ? TestingReport.Passed
                : TestingReport.Blocked;
            combined.Summary = Lang.T($"Skenario \"{scenario.Title}\" versi {scenario.Version}: {combined.Count(TestingReport.Passed)} dari {steps.Count} langkah lolos.", $"Scenario \"{scenario.Title}\" version {scenario.Version}: {combined.Count(TestingReport.Passed)} of {steps.Count} steps passed.");
            combined.TesterModel = node.Model;

            var by = (await settingService.GetRuntimeAsync(cancellationToken)).EffectiveDeveloperName;
            await testScenarios.SaveRunAsync(scenario.Id, combined, by, cancellationToken);
            foreach (var (name, value) in values) report.Values[name] = value;
            if (report.FollowUps.FirstOrDefault(f => f.ScenarioId == scenario.Id || (scenario.SourceFollowUp is { } n && f.Number == n)) is { } followUp)
            {
                followUp.LastRun = TestingReport.FromJson(combined.ToJson());
                followUp.LastRunAt = DateTime.UtcNow;
            }
            await recorder.UpdateThreadAsync(t =>
            {
                t.Status = ThreadStatus.Completed;
                t.TestingMs = (t.TestingMs ?? 0) + clock.ElapsedMilliseconds;
                t.TestingJson = report.ToJson();
                t.TesterTokens += tokens;
                t.TesterModel = node.Model;
            });
            var closing = TestingClosing(combined);
            recorder.Log(combined.Outcome == TestingReport.Passed ? ActivityLevel.Success : ActivityLevel.Warning, Lang.T($"Skenario {scenario.Slug}: {closing}.", $"Scenario {scenario.Slug}: {closing}."));
            await progress.StepAsync(closing, cancellationToken, last: true);
            return new TestingResult(combined, tokens, TesterAgent.ForModels(combined), node);
        }
        catch (Exception ex)
        {
            var cancelled = ex is OperationCanceledException;
            if (!cancelled) logger.LogError(ex, "Scenario {Scenario} of thread {ThreadId} crashed", scenario.Slug, thread.Id);
            recorder.Log(cancelled ? ActivityLevel.Warning : ActivityLevel.Error, cancelled ? Lang.T("Skenario dibatalkan.", "Scenario cancelled.") : Lang.T($"Skenario gagal: {ex.Message}", $"Scenario failed: {ex.Message}"));
            await recorder.UpdateThreadAsync(t => t.Status = ThreadStatus.Completed);
            return null;
        }
        finally
        {
            registry.Complete(thread.Id);
        }
    }
}
