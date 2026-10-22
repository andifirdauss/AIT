using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Verification;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class UnitTestStage
{
    public async Task<UnitTestSession?> OpenAsync(ProjectContext project, RuntimeSettings settings, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(project.Key)) return null;
        // A shared store that cannot be reached must not hold the thread up for a connection timeout.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(CatalogTimeout);
        try
        {
            var unitSettings = await catalog.GetSettingsAsync(project.Key, limit.Token);
            if (unitSettings.Mode == UnitTestMode.Off) return null;
            var suites = TestCommandResolver.Suites(project.RootPath, project.Modules);
            if (suites.Count == 0) return null;

            var tests = (await catalog.ListAsync(project.Key, limit.Token)).ToList();
            // The first thread of a project that already has tests fills the catalog from the code (no model). The
            // store answered by then, so the reads after a scan, which may take a while, are not limited.
            var reads = limit.Token;
            if (tests.Count == 0)
            {
                await scanner.ScanAsync(project, label: false, settings.EffectiveDeveloperName, cancellationToken);
                tests = (await catalog.ListAsync(project.Key, cancellationToken)).ToList();
                reads = cancellationToken;
            }

            var session = new UnitTestSession
            {
                Project = project,
                Settings = unitSettings,
                Suites = suites,
                Tests = tests,
                Timeout = TimeSpan.FromSeconds(unitSettings.TimeoutSeconds ?? settings.BuildTimeoutSeconds),
                Developer = settings.EffectiveDeveloperName,
                MachineRuns = await catalog.LastRunsOnMachineAsync(project.Key, Environment.MachineName, reads)
            };
            session.Report.Mode = unitSettings.Mode.ToString();
            session.Report.Suites = suites.Where(s => s.CanRun).Select(s => $"{s.Framework} · {s.Manifest}").ToList();
            session.Report.Problems = suites.Where(s => !s.CanRun).Select(s => $"{s.Manifest}: {s.Problem}").ToList();
            return session;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Unit tests of {Project} are skipped: the catalog cannot be read.", project.Name);
            return null;
        }
    }
}
