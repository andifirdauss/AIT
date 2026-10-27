using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// The planner pauses for the developer's permission (a command, files outside the project) and goes on with the answer.
public class ApprovalOrchestrationTests
{
    [Fact]
    public async Task A_command_that_needs_permission_pauses_and_runs_once_allowed()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.RunCommand, new { command = "git --version && git --version", reason = "cek versi git" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Git terpasang." }));

        var (first, firstObserver) = await harness.RunAsync("Cek versi git", ThreadSource.ClaudeCode, "session-cmd");

        Assert.Equal(ThreadStatus.AwaitingInput, first.Status);
        Assert.Contains("git --version && git --version", firstObserver.Final);
        Assert.Contains("Izinkan sekali", firstObserver.Final);

        var (second, secondObserver) = await harness.RunAsync("1", ThreadSource.ClaudeCode, "session-cmd");

        Assert.Equal(first.ThreadId, second.ThreadId);
        Assert.Equal(ThreadStatus.Completed, second.Status);
        Assert.Contains("Git terpasang.", secondObserver.Final);
        var toolMessage = router.CallsTo(ScriptedAgent.Planner)[1].Messages.OfType<JsonObject>().Last(m => m["role"]?.GetValue<string>() == "tool");
        Assert.Contains("Exit code 0", toolMessage["content"]!.GetValue<string>());
        // "Once" keeps nothing.
        Assert.Empty(await harness.Service<IApprovalService>().ListAsync(harness.ProjectId));
    }

    [Fact]
    public async Task A_denied_command_tells_the_planner_and_runs_nothing()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.RunCommand, new { command = "npm install", reason = "pasang paket" }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Tidak dipasang." }));

        await harness.RunAsync("Pasang paket", ThreadSource.ClaudeCode, "session-deny");
        var (second, _) = await harness.RunAsync("tolak", ThreadSource.ClaudeCode, "session-deny");

        Assert.Equal(ThreadStatus.Completed, second.Status);
        var toolMessage = router.CallsTo(ScriptedAgent.Planner)[1].Messages.OfType<JsonObject>().Last(m => m["role"]?.GetValue<string>() == "tool");
        Assert.Contains("did not allow this command", toolMessage["content"]!.GetValue<string>());
    }

    [Fact]
    public async Task Files_outside_the_project_are_changed_after_permission_and_always_is_remembered()
    {
        await using var harness = await CreateAsync();
        using var other = new TempWorkspace();
        Directory.CreateDirectory(Path.Combine(other.Root, ".git"));
        var outside = other.Write("src/Greeter.cs", Greeter).Replace('\\', '/');
        var router = harness.Router;
        var plan = new ThreadPlan { Summary = "Sapaan proyek lain jadi Halo.", Tasks = [Step("T1", outside, "Return \"Halo\".")] };
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(plan))
            .OnExecutor(outside, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan proyek lain jadi Halo."));

        var (first, firstObserver) = await harness.RunAsync("Ganti sapaan di proyek lain", ThreadSource.ClaudeCode, "session-out");

        Assert.Equal(ThreadStatus.AwaitingInput, first.Status);
        Assert.Contains(other.Root.Replace('\\', '/'), firstObserver.Final);
        Assert.Contains("Hi", await File.ReadAllTextAsync(outside));

        var (second, _) = await harness.RunAsync("2", ThreadSource.ClaudeCode, "session-out");

        Assert.Equal(ThreadStatus.Completed, second.Status);
        Assert.Contains("Halo", await File.ReadAllTextAsync(outside));
        var grant = Assert.Single(await harness.Service<IApprovalService>().ListAsync(harness.ProjectId));
        Assert.Equal(ApprovalKind.WriteOutside, grant.Kind);

        // The next thread writing there does not ask again.
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Balik ke Hi.", Tasks = [Step("T1", outside, "Return \"Hi\".")] }))
            .OnExecutor(outside, ScriptedRouter.Edit(HalloLine, HelloLine, "Sapaan dikembalikan."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan kembali Hi."));
        var (third, _) = await harness.RunAsync("Kembalikan sapaannya", ThreadSource.ClaudeCode, "session-out");

        Assert.Equal(ThreadStatus.Completed, third.Status);
        Assert.Contains("Hi", await File.ReadAllTextAsync(outside));

        // The outside file is journaled by its absolute path, so the thread can be undone.
        await harness.Rollback.RollbackAsync(third.ThreadId);
        Assert.Contains("Halo", await File.ReadAllTextAsync(outside));
    }
    [Fact]
    public async Task A_verified_change_is_committed_after_the_developer_allows_it()
    {
        await using var harness = await CreateAsync();
        var root = harness.Project.Root;
        Git(root, "init", "-b", "feature");
        Git(root, "config", "user.name", "AIT Test");
        Git(root, "config", "user.email", "ait@test.local");
        Git(root, "config", "commit.gpgsign", "false");
        Git(root, "add", "-A");
        Git(root, "commit", "-m", "init");
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Sapaan jadi Halo.",
                Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")],
                Publish = new PlanPublish { Commit = true, Message = "Ganti sapaan jadi Halo" }
            }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan jadi Halo."));

        var (first, firstObserver) = await harness.RunAsync("Ganti sapaan jadi Halo lalu commit", ThreadSource.ClaudeCode, "session-pub");

        Assert.Equal(ThreadStatus.Completed, first.Status);
        Assert.Contains("Izinkan sekali", firstObserver.Final);
        Assert.Equal("init", Git(root, "log", "-1", "--format=%s"));

        var (second, secondObserver) = await harness.RunAsync("1", ThreadSource.ClaudeCode, "session-pub");

        Assert.Equal(first.ThreadId, second.ThreadId);
        Assert.Contains("Commit", secondObserver.Final);
        Assert.Equal("Ganti sapaan jadi Halo", Git(root, "log", "-1", "--format=%s"));
    }

    private static string Git(string directory, params string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-c", "core.autocrlf=false" }.Concat(arguments)) startInfo.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(startInfo)!;
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, $"git {string.Join(' ', arguments)} failed: {error.Result}");
        return output.Trim();
    }
}
