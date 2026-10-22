using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using AIThread.Service.UnitTests;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;

namespace AIThread.Service.Implementations;

public sealed partial class UnitTestService
{
    public async Task<UnitTestRunResultDto> RunAsync(Guid projectId, RunUnitTestsRequest request, CancellationToken cancellationToken = default)
    {
        var project = await ContextAsync(projectId, cancellationToken);
        var runtime = await settingService.GetRuntimeAsync(cancellationToken);
        var settings = await catalog.GetSettingsAsync(project.Key, cancellationToken);
        var all = await catalog.ListAsync(project.Key, cancellationToken);
        var wanted = request.Ids?.ToHashSet() ?? [];
        // Every active test, or the chosen ones whatever their state (a quarantined test can be tried by hand).
        var tests = (request.All ? all.Where(t => t.State == UnitTestState.Active) : all.Where(t => wanted.Contains(t.Id) && t.State != UnitTestState.Removed)).ToList();
        if (tests.Count == 0) throw new AppValidationException("Tidak ada unit test yang dipilih untuk dijalankan.");

        var suites = TestCommandResolver.Suites(project.RootPath, project.Modules);
        var timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds ?? runtime.BuildTimeoutSeconds);
        var execution = await executor.RunAsync(project.RootPath, suites, tests, timeout, retryFailures: true, cancellationToken);

        var now = DateTime.UtcNow;
        var by = runtime.EffectiveDeveloperName;
        var (commit, branch) = settings.KeepSnapshots && GitTools.IsRepository(project.RootPath)
            ? await new GitTools(new WorkspaceScope(project.RootPath)).HeadAsync(cancellationToken)
            : (null, null);
        var runs = new List<UnitTestRun>();
        foreach (var test in tests)
        {
            var verdict = execution.Verdicts[test.Id];
            UnitTestStage.Apply(test, verdict, UnitTestText.InputHash(project.RootPath, test), now, by);
            if (verdict.Result == UnitTestResult.Passed && settings.KeepSnapshots)
                UnitTestSnapshot.Capture(project.RootPath, test, TestCommandResolver.SuiteFor(suites, project.RootPath, test.TestFile), commit, branch);
            runs.Add(new UnitTestRun
            {
                UnitTestCaseId = test.Id,
                ProjectKey = project.Key,
                Phase = verdict.Flaky ? UnitTestRun.Flaky : UnitTestRun.Manual,
                Result = verdict.Result,
                DurationMs = verdict.DurationMs,
                Message = verdict.Message is null ? null : UnitTestText.Clip(verdict.Message, 2_000),
                SkipReason = verdict.SkipReason is null ? null : UnitTestText.Clip(verdict.SkipReason, 500),
                Machine = Environment.MachineName,
                RunBy = by
            });
        }
        await catalog.UpdateAsync(tests, cancellationToken);
        await catalog.RecordRunsAsync(runs, cancellationToken);

        var items = tests.Select(t => (Test: t, Verdict: execution.Verdicts[t.Id])).ToList();
        var passed = items.Count(i => i.Verdict.Result == UnitTestResult.Passed);
        var failed = items.Count(i => i.Verdict.IsFailure);
        activityLog.Write(failed > 0 ? ActivityLevel.Warning : ActivityLevel.Success, "unittest",
            $"Unit test {project.Name} dijalankan manual: {tests.Count} test, {passed} lolos, {failed} gagal, {tests.Count - passed - failed} dilewati.", projectId: projectId);
        return new UnitTestRunResultDto(tests.Count, passed, failed, tests.Count - passed - failed, execution.DurationMs,
            items.Select(i => new UnitTestRunItemDto(i.Test.Id, i.Test.DisplayNumber, i.Test.Title, i.Verdict.Result, i.Verdict.DurationMs, i.Verdict.Message, i.Verdict.SkipReason)).ToList(),
            suites.Where(s => !s.CanRun).Select(s => $"{s.Manifest}: {s.Problem}").ToList());
    }
}
