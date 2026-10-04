using AiThread.Common.Enums;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using static AiThread.Tests.ThreadOrchestratorTests;

namespace AiThread.Tests;

// Retrying the unfinished tasks of a thread from its stored plan, and undoing a thread's file changes.
public class ThreadRecoveryTests
{
    private const string MissingLine = "    public string Goodbye() => \"Bye\";";

    private static readonly ThreadPlan TwoTasks = new()
    {
        Summary = "Ganti sapaan dan tambah Farewell.",
        Tasks =
        [
            Step("T1", GreeterFile, "Return \"Halo\" from Hello()."),
            Step("T2", FarewellFile, "Create a Farewell class.", "create", "T1")
        ]
    };

    [Fact]
    public async Task Retry_runs_only_the_unfinished_tasks_without_planning_again()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(TwoTasks))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(MissingLine, HalloLine, "x"))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(MissingLine, HalloLine, "x"))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Gagal."));
        var (first, _) = await harness.RunAsync("Ganti sapaan dan tambah Farewell");
        Assert.Equal(ThreadStatus.Failed, first.Status);

        router
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .OnExecutor(FarewellFile, ScriptedRouter.Create("public class Farewell { }", "Farewell dibuat."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Berhasil setelah diulang."));

        var (retried, observer) = await harness.RetryAsync(first.ThreadId);

        Assert.Equal(first.ThreadId, retried.ThreadId);
        Assert.Equal(ThreadStatus.Completed, retried.Status);
        Assert.Single(router.CallsTo(ScriptedAgent.Planner));
        Assert.Contains(HalloLine, harness.ReadFile(GreeterFile));
        Assert.Contains("public class Farewell", harness.ReadFile(FarewellFile));
        Assert.Contains(observer.Lines, l => l.Contains("Mengulang 2 tugas"));

        var thread = await harness.LoadThreadAsync(first.ThreadId);
        Assert.Equal(1, thread.RetryCount);
        Assert.Null(thread.Error);
        Assert.Equal("Berhasil setelah diulang.", thread.FinalResponse);
        var tasks = thread.Tasks.OrderBy(t => t.Order).ToList();
        Assert.Equal(["T1", "T2"], tasks.Select(t => t.TaskKey));
        Assert.All(tasks, t => Assert.Equal(ThreadTaskStatus.Succeeded, t.Status));
        Assert.Equal(1, tasks[0].Attempts);
    }

    [Fact]
    public async Task Retry_keeps_tasks_that_succeeded()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Dua file.",
                Tasks =
                [
                    Step("T1", GreeterFile, "Return \"Halo\"."),
                    Step("T2", FarewellFile, "Create Farewell.", "create")
                ]
            }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .OnExecutor(FarewellFile, _ => throw new NineRouterException("Bad gateway", 502))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sebagian."));
        var (first, _) = await harness.RunAsync("Dua file");
        Assert.Equal(ThreadStatus.Completed, first.Status);

        router
            .OnExecutor(FarewellFile, ScriptedRouter.Create("public class Farewell { }", "Farewell dibuat."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Lengkap."));
        var (retried, _) = await harness.RetryAsync(first.ThreadId);

        Assert.Equal(ThreadStatus.Completed, retried.Status);
        // T1 is not run again: one executor call for Greeter in total.
        Assert.Single(router.CallsTo(ScriptedAgent.Executor), c => c.LastUser.Contains("Target file: " + GreeterFile));
        // The wrapper of the retry reports both tasks.
        var wrapInput = router.CallsTo(ScriptedAgent.Wrapper)[^1].LastUser;
        Assert.Contains(GreeterFile, wrapInput);
        Assert.Contains(FarewellFile, wrapInput);
    }

    [Fact]
    public async Task Retry_creates_the_tasks_of_a_thread_that_failed_before_executing()
    {
        await using var harness = await CreateAsync(addNodes: false);
        await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        harness.Router.On(ScriptedAgent.Planner, harness.Router.SubmitPlan(new ThreadPlan
        {
            Summary = "Sapaan jadi Halo.",
            Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")]
        }));
        var (first, _) = await harness.RunAsync("Sapaan jadi Halo");
        Assert.Equal(ThreadStatus.Failed, first.Status);
        Assert.Empty((await harness.LoadThreadAsync(first.ThreadId)).Tasks);

        // The missing executor is added; retrying needs no new plan.
        await harness.AddNodeAsync(NodeRole.Executor, "executor-model");
        harness.Router.OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."));
        var (retried, _) = await harness.RetryAsync(first.ThreadId);

        Assert.Equal(ThreadStatus.Completed, retried.Status);
        Assert.Contains(HalloLine, harness.ReadFile(GreeterFile));
        Assert.Equal(ThreadTaskStatus.Succeeded, Assert.Single((await harness.LoadThreadAsync(first.ThreadId)).Tasks).Status);
    }

    [Fact]
    public async Task Retry_is_refused_when_nothing_is_left_to_do()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "x", Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")] }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "ok"))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("ok"))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "y", Answer = "Jawaban." }));
        var (done, _) = await harness.RunAsync("Sapaan jadi Halo");
        var (answered, _) = await harness.RunAsync("Pertanyaan saja");

        var allDone = await Assert.ThrowsAsync<AppValidationException>(() => harness.RetryAsync(done.ThreadId));
        Assert.Contains("sudah berhasil", allDone.Message);
        var noTasks = await Assert.ThrowsAsync<AppValidationException>(() => harness.RetryAsync(answered.ThreadId));
        Assert.Contains("tidak punya rencana", noTasks.Message);
    }

    [Fact]
    public async Task Rollback_restores_edited_files_and_deletes_created_ones()
    {
        await using var harness = await CreateAsync();
        var original = await File.ReadAllBytesAsync(Path.Combine(harness.Project.Root, "src", "Greeter.cs"));
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(TwoTasks))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .OnExecutor(FarewellFile, ScriptedRouter.Create("public class Farewell { }", "Farewell dibuat."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));
        var (result, _) = await harness.RunAsync("Ganti sapaan dan tambah Farewell");

        var preview = await harness.Rollback.PreviewAsync(result.ThreadId);
        Assert.NotNull(preview);
        Assert.True(preview.CanRollback, preview.Reason);
        Assert.Equal(ThreadRollback.Delete, preview.Files.Single(f => f.FilePath == FarewellFile).Action);
        Assert.Equal(ThreadRollback.Restore, preview.Files.Single(f => f.FilePath == GreeterFile).Action);

        var outcome = await harness.Rollback.RollbackAsync(result.ThreadId);

        Assert.Equal((1, 1), (outcome.Restored, outcome.Deleted));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(harness.Project.Root, "src", "Greeter.cs")));
        Assert.False(File.Exists(Path.Combine(harness.Project.Root, "src", "Farewell.cs")));

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal(ThreadStatus.RolledBack, thread.Status);
        Assert.NotNull(thread.RolledBackAt);

        // Once undone, a thread can be neither undone again nor retried.
        Assert.False((await harness.Rollback.PreviewAsync(result.ThreadId))!.CanRollback);
        await Assert.ThrowsAsync<AppValidationException>(() => harness.Rollback.RollbackAsync(result.ThreadId));
        var retry = await Assert.ThrowsAsync<AppValidationException>(() => harness.RetryAsync(result.ThreadId));
        Assert.Contains("rollback", retry.Message);
    }

    [Fact]
    public async Task Rollback_is_refused_as_a_whole_when_a_file_changed_afterwards()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(TwoTasks))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .OnExecutor(FarewellFile, ScriptedRouter.Create("public class Farewell { }", "Farewell dibuat."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));
        var (result, _) = await harness.RunAsync("Ganti sapaan dan tambah Farewell");

        // The developer keeps working on the file after the thread.
        var edited = harness.ReadFile(GreeterFile) + "// edited by hand\n";
        harness.Project.Write(GreeterFile, edited);

        var preview = (await harness.Rollback.PreviewAsync(result.ThreadId))!;
        Assert.False(preview.CanRollback);
        Assert.Equal(ThreadRollback.Conflict, preview.Files.Single(f => f.FilePath == GreeterFile).Action);
        Assert.Contains(GreeterFile, preview.Reason);

        var refused = await Assert.ThrowsAsync<AppValidationException>(() => harness.Rollback.RollbackAsync(result.ThreadId));
        Assert.Contains("ditolak", refused.Message);

        // Nothing was touched: the created file and the hand edit are both still there.
        Assert.Equal(edited, harness.ReadFile(GreeterFile));
        Assert.True(File.Exists(Path.Combine(harness.Project.Root, "src", "Farewell.cs")));
        Assert.Equal(ThreadStatus.Completed, (await harness.LoadThreadAsync(result.ThreadId)).Status);
    }

    [Fact]
    public async Task Rollback_returns_to_the_state_before_the_thread_across_fix_rounds_and_retries()
    {
        await using var harness = await CreateAsync();
        await harness.AddNodeAsync(NodeRole.Reviewer, "reviewer-model");
        var original = await File.ReadAllBytesAsync(Path.Combine(harness.Project.Root, "src", "Greeter.cs"));
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Sapaan jadi Halo, lalu Farewell.",
                Tasks =
                [
                    Step("T1", GreeterFile, "Return \"Halo\"."),
                    Step("T2", FarewellFile, "Create Farewell.", "create")
                ]
            }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .OnExecutor(FarewellFile, _ => throw new NineRouterException("Bad gateway", 502))
            .On(ScriptedAgent.Reviewer, ScriptedRouter.Text("""
                {"approved": false, "issues": [{"file": "src/Greeter.cs", "problem": "Missing exclamation mark.", "fix": "Return \"Halo!\"."}]}
                """))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HalloLine, """    public string Hello() => "Halo!";""", "Tanda seru."))
            .On(ScriptedAgent.Reviewer, ScriptedRouter.Text("""{"approved": true, "issues": []}"""))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sebagian."));
        var (result, _) = await harness.RunAsync("Sapaan dan Farewell");

        router
            .OnExecutor(FarewellFile, ScriptedRouter.Create("public class Farewell { }", "Farewell dibuat."))
            .On(ScriptedAgent.Reviewer, ScriptedRouter.Text("""{"approved": true, "issues": []}"""))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Lengkap."));
        await harness.RetryAsync(result.ThreadId);

        // The retry's verification continues the round numbering, so fix task ids never repeat.
        var keys = (await harness.LoadThreadAsync(result.ThreadId)).Tasks.Select(t => t.TaskKey).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());

        var outcome = await harness.Rollback.RollbackAsync(result.ThreadId);

        Assert.Equal((1, 1), (outcome.Restored, outcome.Deleted));
        Assert.Equal(original, await File.ReadAllBytesAsync(Path.Combine(harness.Project.Root, "src", "Greeter.cs")));
        Assert.False(File.Exists(Path.Combine(harness.Project.Root, "src", "Farewell.cs")));
    }

    [Fact]
    public async Task Thread_without_file_changes_has_nothing_to_undo()
    {
        await using var harness = await CreateAsync();
        harness.Router.On(ScriptedAgent.Planner, harness.Router.SubmitPlan(new ThreadPlan { Summary = "x", Answer = "Jawaban." }));
        var (result, _) = await harness.RunAsync("Pertanyaan saja");

        var preview = (await harness.Rollback.PreviewAsync(result.ThreadId))!;

        Assert.False(preview.CanRollback);
        Assert.Contains("tidak mengubah file", preview.Reason);
        Assert.Null(await harness.Rollback.PreviewAsync(Guid.NewGuid()));
    }
}
