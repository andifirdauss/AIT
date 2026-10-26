using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

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
        Assert.Contains(observer.Lines, l => l.Contains("Percobaan ulang · 2 tugas"));

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
    public async Task Ulangi_in_the_chat_retries_the_last_thread_without_planning_again()
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
            .OnExecutor(FarewellFile, _ => throw new NineRouterException("Model sedang gangguan", 503))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sebagian."));
        var (first, firstObserver) = await harness.RunAsync("Dua file", ThreadSource.ClaudeCode, "chat-1");
        Assert.Contains("Balas `ulangi`", firstObserver.Final);

        router
            .OnExecutor(FarewellFile, ScriptedRouter.Create("public class Farewell { }", "Farewell dibuat."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Lengkap."));
        var (retried, observer) = await RunInChatAsync(harness, "Ulangi yang gagal", first.ThreadId);

        Assert.Equal(first.ThreadId, retried.ThreadId);
        Assert.Equal(ThreadStatus.Completed, retried.Status);
        Assert.Single(router.CallsTo(ScriptedAgent.Planner));
        Assert.Contains("public class Farewell", harness.ReadFile(FarewellFile));
        // The reply carries the thread's anchor, so the chat still knows the thread.
        Assert.Equal([ThreadAnchor.ShortId(first.ThreadId)], ThreadAnchor.Find(retried.FinalText));
        Assert.Contains(observer.Lines, l => l.Contains("Percobaan ulang · 1 tugas"));
        Assert.DoesNotContain("Balas `ulangi`", observer.Final);
        Assert.Equal(1, (await harness.LoadThreadAsync(first.ThreadId)).RetryCount);
    }

    [Fact]
    public async Task Ulangi_without_unfinished_tasks_in_the_chat_is_a_normal_prompt()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "x", Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")] }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "ok"))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("ok"))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "y", Answer = "Tidak ada yang perlu diulang." }));
        var (done, doneObserver) = await harness.RunAsync("Sapaan jadi Halo", ThreadSource.ClaudeCode, "chat-1");
        Assert.DoesNotContain("Balas `ulangi`", doneObserver.Final);

        var (next, _) = await RunInChatAsync(harness, "ulangi", done.ThreadId);

        Assert.NotEqual(done.ThreadId, next.ThreadId);
        Assert.Equal(2, router.CallsTo(ScriptedAgent.Planner).Count);
    }

    [Theory]
    [InlineData("ulangi", true)]
    [InlineData("Ulangi yang gagal.", true)]
    [InlineData("coba lagi", true)]
    [InlineData("retry!", true)]
    [InlineData("ulangi tugas yang gagal", true)]
    [InlineData("Retry failed tasks", true)]
    [InlineData("try again.", true)]
    [InlineData("retry the failed task", true)]
    [InlineData("retry with another approach", false)]
    [InlineData("ulangi validasi di UserService", false)]
    [InlineData("coba lagi dengan cara lain", false)]
    [InlineData("", false)]
    public void Retry_reply_is_only_a_short_reply_of_its_own(string reply, bool expected) =>
        Assert.Equal(expected, RetryReply.IsMatch(reply));

    // A chat turn whose history holds the anchor of threadId, like Claude Code sends it.
    private static async Task<(ThreadResult Result, RecordingObserver Observer)> RunInChatAsync(OrchestrationHarness harness, string prompt, Guid threadId)
    {
        var observer = new RecordingObserver();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var request = new ThreadRequest(ThreadSource.ClaudeCode, prompt, prompt, null, harness.Project.Root, harness.ProjectId, "chat-1",
            new SessionContext(null, null, [threadId]));
        return (await harness.Orchestrator.RunAsync(request, observer, timeout.Token), observer);
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
    public async Task Move_and_delete_run_without_a_model_and_rollback_brings_the_files_back()
    {
        const string movedFile = "src/Core/Greeting/Greeter.cs";
        await using var harness = await CreateAsync();
        var root = harness.Project.Root;
        harness.Project.Write("src/Old.cs", "public class Old { }");
        var greeter = await File.ReadAllBytesAsync(Path.Combine(root, "src", "Greeter.cs"));
        var old = await File.ReadAllBytesAsync(Path.Combine(root, "src", "Old.cs"));

        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Pindahkan Greeter, ganti sapaan, hapus Old.",
                Tasks =
                [
                    new PlanTask { Id = "T1", Title = "Move Greeter", File = GreeterFile, Action = "move", To = movedFile },
                    Step("T2", movedFile, "Return \"Halo\" from Hello().", "edit", "T1"),
                    new PlanTask { Id = "T3", Title = "Delete Old", File = "src/Old.cs", Action = "delete" }
                ]
            }))
            .OnExecutor(movedFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));
        var (result, observer) = await harness.RunAsync("Pindahkan Greeter ke Core/Greeting dan hapus Old");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Single(router.CallsTo(ScriptedAgent.Executor)); // only the edit needs a model
        Assert.False(File.Exists(Path.Combine(root, "src", "Greeter.cs")));
        Assert.False(File.Exists(Path.Combine(root, "src", "Old.cs")));
        Assert.Contains(HalloLine, harness.ReadFile(movedFile));
        Assert.Contains(observer.Lines, l => l.Contains($"{GreeterFile} → {movedFile}"));

        await using (var db = await harness.CreateDbAsync())
        {
            var move = db.ThreadTasks.Single(t => t.ThreadRunId == result.ThreadId && t.TaskKey == "T1");
            Assert.Equal((ThreadTaskStatus.Succeeded, movedFile, (string?)null), (move.Status, move.TargetPath, move.Model));
        }

        var preview = (await harness.Rollback.PreviewAsync(result.ThreadId))!;
        Assert.True(preview.CanRollback, preview.Reason);
        Assert.Equal(ThreadRollback.Restore, preview.Files.Single(f => f.FilePath == GreeterFile).Action);
        Assert.Equal(ThreadRollback.Restore, preview.Files.Single(f => f.FilePath == "src/Old.cs").Action);
        Assert.Equal(ThreadRollback.Delete, preview.Files.Single(f => f.FilePath == movedFile).Action);

        var outcome = await harness.Rollback.RollbackAsync(result.ThreadId);

        Assert.Equal((2, 1), (outcome.Restored, outcome.Deleted));
        Assert.Equal(greeter, await File.ReadAllBytesAsync(Path.Combine(root, "src", "Greeter.cs")));
        Assert.Equal(old, await File.ReadAllBytesAsync(Path.Combine(root, "src", "Old.cs")));
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Core")), "the folders the move created are removed again");
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
