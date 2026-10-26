using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    // Results before the change for the tests that have none yet: an environment skip remembered on this machine, an
    // earlier result of the same code, or a run now. Never throws; a test it could not run has no baseline.
    private async Task BaselineAsync(UnitTestSession session, IReadOnlyList<UnitTestCase> tests, ThreadRecorder recorder, CancellationToken cancellationToken)
    {
        try
        {
            var toRun = new List<UnitTestCase>();
            foreach (var test in tests)
            {
                if (session.Baseline.ContainsKey(test.Id)) continue;
                if (EnvironmentSkip(session, test) is { } skip)
                {
                    session.Baseline[test.Id] = skip;
                    continue;
                }
                if (Cached(session, test) is { } cached)
                {
                    session.Baseline[test.Id] = cached;
                    session.Cached[test.Id] = true;
                    continue;
                }
                toRun.Add(test);
            }
            if (toRun.Count == 0) return;

            var execution = await executor.RunAsync(session.Project.RootPath, session.Suites, toRun, session.Timeout, retryFailures: false, cancellationToken);
            foreach (var (id, verdict) in execution.Verdicts) session.Baseline.TryAdd(id, verdict);
            lock (session.Report)
            {
                session.Report.BaselineRan += toRun.Count;
                session.Report.BaselineMs += execution.DurationMs;
            }
            recorder.Log(ActivityLevel.Info, Lang.T($"Baseline unit test: {toRun.Count} test dijalankan sebelum perubahan ({execution.DurationMs / 1000.0:0.0}s).", $"Unit test baseline: {toRun.Count} tests run before the change ({execution.DurationMs / 1000.0:0.0}s)."));
        }
        catch (OperationCanceledException)
        {
            // The thread was cancelled; it reports that itself.
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "The unit test baseline of {Project} failed.", session.Project.Name);
            recorder.Log(ActivityLevel.Warning, Lang.T($"Baseline unit test gagal: {ex.Message}", $"Unit test baseline failed: {ex.Message}"));
        }
    }
}
