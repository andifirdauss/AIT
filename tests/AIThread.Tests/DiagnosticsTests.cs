using System.IO.Compression;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Diagnostics;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using Microsoft.Extensions.Logging;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// Log menu: incidents with stack trace and companion files, the logger that records them, and the diagnostic ZIP.
public class DiagnosticsTests
{
    [Fact]
    public async Task A_failed_executor_leaves_an_incident_with_its_raw_output()
    {
        using var _ = Lang.Use("en");
        await using var harness = await OrchestrationHarness.CreateAsync(s => s.ExecutorMaxRetries = 1);
        harness.Project.Write(GreeterFile, Greeter);
        await harness.AddStandardNodesAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Halo.", Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")] }))
            .OnExecutor(GreeterFile, ScriptedRouter.Text("Sure! I will change the greeting to Halo."))
            .OnExecutor(GreeterFile, ScriptedRouter.Text("@@ -1 +1 @@\n-Hi\n+Halo"))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Failed."));

        var (result, _) = await harness.RunAsync("Ganti sapaan jadi Halo", ThreadSource.Dashboard);

        var task = (await harness.LoadThreadAsync(result.ThreadId)).Tasks.Single();
        Assert.Equal(ThreadTaskStatus.Failed, task.Status);
        Assert.Contains("unified diff", task.Error);

        // The retry told the model what was wrong, not only "no valid block".
        var retry = router.CallsTo(ScriptedAgent.Executor)[1];
        Assert.Contains("only text", retry.LastUser);

        var diagnostics = harness.Service<IDiagnosticsService>();
        var summary = Assert.Single(await diagnostics.ListIncidentsAsync(result.ThreadId, null, 10));
        Assert.Equal("executor", summary.Category);
        // The task error points at the incident (Log menu).
        Assert.Contains(summary.Id, task.Error);
        var incident = (await diagnostics.GetIncidentAsync(summary.Id))!;
        Assert.Equal(["attempt-1-feedback.txt", "attempt-1-output.txt", "attempt-2-feedback.txt", "attempt-2-output.txt", "task-message.md", "task.md"],
            incident.Files.Select(f => f.Name).ToArray());
        Assert.Equal("Sure! I will change the greeting to Halo.", await diagnostics.ReadIncidentFileAsync(summary.Id, "attempt-1-output.txt"));
        Assert.Null(await diagnostics.ReadIncidentFileAsync(summary.Id, "../incident.json"));
    }

    [Fact]
    public async Task An_error_logged_with_an_exception_becomes_one_incident_and_repeats_fold_into_it()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var logger = harness.Service<ILoggerFactory>().CreateLogger("AIThread.Service.Orchestration.ThreadOrchestrator");
        var thread = Guid.NewGuid();
        var failure = new InvalidOperationException("An error occurred while saving the entity changes.",
            new InvalidOperationException("String or binary data would be truncated in column 'Target', row 1."));

        logger.LogError(failure, "Thread {ThreadId} crashed", thread);
        logger.LogError(failure, "Thread {ThreadId} crashed", thread);
        logger.LogWarning(new OperationCanceledException(), "cancelled");
        logger.LogError("no exception, no incident");

        var diagnostics = harness.Service<IDiagnosticsService>();
        var summary = Assert.Single(await diagnostics.ListIncidentsAsync(null, null, 10));
        Assert.Equal(2, summary.Count);
        Assert.Equal(thread, summary.ThreadId);
        Assert.Contains("truncated in column 'Target'", summary.Title);
        var incident = (await diagnostics.GetIncidentAsync(summary.Id))!;
        Assert.Contains("--->", incident.StackTrace);
        Assert.Single(await diagnostics.ListIncidentsAsync(null, "binary data", 10));
        Assert.Empty(await diagnostics.ListIncidentsAsync(null, "no such text", 10));
    }

    [Fact]
    public async Task The_export_zip_holds_incidents_activity_and_run_logs_with_secrets_masked()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var diagnostics = harness.Service<IDiagnosticsService>();
        Directory.CreateDirectory(diagnostics.LogRoot);
        await File.WriteAllTextAsync(Path.Combine(diagnostics.LogRoot, "aithread-20261028-120000.log"),
            "12:00:01 FAIL Worker: failed\n    Server=db;Password=Sup3r-Secret!;Database=x\n");
        diagnostics.Record(new DiagnosticReport("Error", "executor", "T1 failed", Files: [new DiagnosticAttachment("attempt-1-output.txt", "api_key: \"sk-live-123456789abcdef\"")]));

        using var zipStream = new MemoryStream();
        await diagnostics.ExportAsync(new DiagnosticExportRequest(Days: 1), zipStream);
        zipStream.Position = 0;
        using var zip = new ZipArchive(zipStream);
        var names = zip.Entries.Select(e => e.FullName).ToList();

        Assert.Contains("README.md", names);
        Assert.Contains("activity.log", names);
        Assert.Contains("logs/aithread-20261028-120000.log", names);
        Assert.Contains(names, n => n.StartsWith("incidents/") && n.EndsWith("/incident.md"));
        Assert.Contains(names, n => n.EndsWith("/files/attempt-1-output.txt"));
        var all = string.Concat(zip.Entries.Select(e => new StreamReader(e.Open()).ReadToEnd()));
        Assert.DoesNotContain("Sup3r-Secret!", all);
        Assert.DoesNotContain("sk-live-123456789abcdef", all);
        Assert.Contains("[secret:", all);
    }

    [Fact]
    public async Task Deleting_an_incident_removes_its_folder_and_ids_outside_the_folder_are_refused()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var diagnostics = harness.Service<IDiagnosticsService>();
        var id = diagnostics.Record(new DiagnosticReport("Error", "test", "boom"))!;

        Assert.Null(await diagnostics.GetIncidentAsync(".."));
        Assert.Equal(0, diagnostics.DeleteIncidents("..\\x"));
        Assert.Equal(1, diagnostics.DeleteIncidents(id));
        Assert.Empty(await diagnostics.ListIncidentsAsync(null, null, 10));
    }
}
