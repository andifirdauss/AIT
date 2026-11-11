using System.Text.Json.Nodes;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// commit_tasks: batches of final tasks before the whole plan, executed while the planner goes on (early execution) or
// with the final plan, and the planner reading how its committed tasks ended.
public class EarlyExecutionTests
{
    private const string Farewell = "namespace Demo;\n\npublic class Farewell { }";
    private const string FarewellLine = "public class Farewell { }";
    private const string FarewellWithBye = "public class Farewell { public string Bye() => \"Dah\"; }";

    private static async Task<OrchestrationHarness> CreateAsync(bool earlyExecution = true)
    {
        var harness = await OrchestrationHarness.CreateAsync(s => s.EarlyExecution = earlyExecution);
        harness.Project.Write(GreeterFile, Greeter);
        await harness.AddStandardNodesAsync();
        return harness;
    }

    // The planner keeps looking around until "# Committed task results" reaches it, then submits the rest.
    private static void UntilResults(ScriptedRouter router, Func<RouterCall, ChatCompletionResult> submit, int turns = 40)
    {
        for (var i = 0; i < turns; i++)
            router.On(ScriptedAgent.Planner, call =>
            {
                if (HasResults(call)) return submit(call);
                Thread.Sleep(50);
                return router.Tool(PlannerTools.ListDirectory, new { path = "src" });
            });
    }

    private static string Messages(RouterCall call) => call.Messages.ToJsonString();

    // The system prompt names the note too: only a user message carrying it counts.
    private static bool HasResults(RouterCall call) =>
        call.Messages.OfType<JsonObject>().Any(m => m["role"]?.GetValue<string>() == "user"
                                                    && m["content"] is JsonValue content && content.GetValue<string>().StartsWith("# Committed task results", StringComparison.Ordinal));

    [Fact]
    public async Task Committed_batch_runs_while_the_planner_plans_and_the_planner_reads_its_result()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router.On(ScriptedAgent.Planner, router.Tool(PlannerTools.CommitTasks, new
        {
            summary = "Ganti sapaan dan tambah Farewell.",
            contract = new[] { new { name = "Greeting", value = "Halo" } },
            tasks = new[] { Step("T1", GreeterFile, "Return \"Halo\" from Hello().") }
        }));
        UntilResults(router, _ => router.SubmitPlan(new ThreadPlan
        {
            Summary = "Ganti sapaan dan tambah Farewell.",
            Tasks = [Step("T2", FarewellFile, "Create a Farewell class.", "create", "T1")]
        }));
        router
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .OnExecutor(FarewellFile, ScriptedRouter.Create(Farewell, "Farewell dibuat."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));

        var (result, observer) = await harness.RunAsync("Ganti sapaan jadi Halo dan tambah class Farewell");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains(HalloLine, harness.ReadFile(GreeterFile));
        Assert.Contains(FarewellLine, harness.ReadFile(FarewellFile));

        // T1 ran before the plan was complete, and the planner was told how it went.
        var calls = router.Calls.ToList();
        var greeterRun = calls.FindIndex(c => c.Agent == ScriptedAgent.Executor && Messages(c).Contains(GreeterFile));
        var submit = calls.FindLastIndex(c => c.Agent == ScriptedAgent.Planner);
        Assert.InRange(greeterRun, 0, submit - 1);
        Assert.Contains("T1 (src/Greeter.cs) succeeded: Sapaan diganti.", Messages(calls[submit]));
        // Its executor saw the contract as committed with its batch.
        Assert.Contains("Halo", Messages(calls[greeterRun]));
        Assert.Contains(observer.Lines, l => l.Contains("📦 batch 1 · 1 tugas langsung dijalankan"));

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal(["T1", "T2"], thread.Tasks.OrderBy(t => t.Order).Select(t => t.TaskKey));
        Assert.All(thread.Tasks, t => Assert.Equal(ThreadTaskStatus.Succeeded, t.Status));
        var plan = Assert.IsType<ThreadPlan>(ThreadPlan.TryParse(thread.PlanJson!, out var saved, out _) ? saved : null);
        Assert.Equal(["T1", "T2"], plan.Tasks.Select(t => t.Id));
        Assert.Contains(plan.Contract, c => c.Value == "Halo");
    }

    [Fact]
    public async Task Without_early_execution_batches_wait_for_the_final_plan()
    {
        await using var harness = await CreateAsync(earlyExecution: false);
        var router = harness.Router;
        var executorsBeforeSubmit = -1;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.CommitTasks, new
            {
                summary = "Ganti sapaan dan tambah Farewell.",
                tasks = new[] { Step("T1", GreeterFile, "Return \"Halo\" from Hello().") }
            }))
            .On(ScriptedAgent.Planner, _ =>
            {
                executorsBeforeSubmit = router.CallsTo(ScriptedAgent.Executor).Count;
                return router.SubmitPlan(new ThreadPlan { Summary = "", Tasks = [Step("T2", FarewellFile, "Create a Farewell class.", "create")] });
            })
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .OnExecutor(FarewellFile, ScriptedRouter.Create(Farewell, "Farewell dibuat."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));

        var (result, observer) = await harness.RunAsync("Ganti sapaan jadi Halo dan tambah class Farewell");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Equal(0, executorsBeforeSubmit);
        Assert.Contains(HalloLine, harness.ReadFile(GreeterFile));
        Assert.Contains(FarewellLine, harness.ReadFile(FarewellFile));
        Assert.Contains(observer.Lines, l => l.Contains("📦 batch 1 · 1 tugas menunggu rencana akhir"));
        // The batch's summary stays the plan's when the final one leaves it out.
        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal("Ganti sapaan dan tambah Farewell.", thread.PlanSummary);
    }

    [Fact]
    public async Task Committed_ids_cannot_be_sent_again_and_a_later_task_edits_a_file_an_earlier_batch_created()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router.On(ScriptedAgent.Planner, router.Tool(PlannerTools.CommitTasks, new
        {
            summary = "Tambah Farewell lalu method Bye.",
            tasks = new[] { Step("T1", FarewellFile, "Create a Farewell class.", "create") }
        }));
        // Sending T1 again is refused; the follow-up edit of the new file is not "does not exist".
        var submits = 0;
        UntilResults(router, _ => ++submits == 1
            ? router.SubmitPlan(new ThreadPlan
            {
                Summary = "Tambah Farewell lalu method Bye.",
                Tasks = [Step("T1", FarewellFile, "Create a Farewell class.", "create"), Step("T2", FarewellFile, "Add Bye().")]
            })
            : router.SubmitPlan(new ThreadPlan
            {
                Summary = "Tambah Farewell lalu method Bye.",
                Tasks = [Step("T2", FarewellFile, "Add Bye().")]
            }));
        router
            .OnExecutor(FarewellFile, ScriptedRouter.Create(Farewell, "Farewell dibuat."))
            .OnExecutor(FarewellFile, ScriptedRouter.Edit(FarewellLine, FarewellWithBye, "Bye ditambahkan."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));

        var (result, _) = await harness.RunAsync("Tambah class Farewell dengan method Bye");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains(FarewellWithBye, harness.ReadFile(FarewellFile));
        var last = router.CallsTo(ScriptedAgent.Planner)[^1];
        Assert.Contains("T1 were already committed with commit_tasks", Messages(last));
        Assert.DoesNotContain("does not exist", Messages(last));

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        ThreadPlan.TryParse(thread.PlanJson!, out var plan, out _);
        Assert.Equal(["T1"], plan!.Tasks.Single(t => t.Id == "T2").After);
    }

    [Fact]
    public async Task A_batch_needs_a_summary_first_and_is_checked_like_a_plan()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.CommitTasks, new { tasks = new[] { Step("T1", GreeterFile, "Return \"Halo\".") } }))
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.CommitTasks, new { summary = "Ubah.", tasks = new[] { Step("T1", "src/Missing.cs", "Edit it.") } }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Tidak ada yang diubah." }));

        var (result, _) = await harness.RunAsync("Ubah sesuatu");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        var calls = router.CallsTo(ScriptedAgent.Planner);
        Assert.Contains("is required with the first batch", Messages(calls[1]));
        Assert.Contains("Batch rejected; nothing of it was committed", Messages(calls[2]));
        Assert.Contains("does not exist", Messages(calls[2]));
        Assert.Empty(router.CallsTo(ScriptedAgent.Executor));
        Assert.Equal(Greeter.ReplaceLineEndings("\n"), harness.ReadFile(GreeterFile));
    }

    // A question after a batch ran: the batch finishes before the thread waits, and the resumed planner goes on with it
    // (its tasks count, a later task may depend on them, the final report has them all).
    [Fact]
    public async Task A_question_after_a_committed_batch_waits_for_it_and_the_resumed_plan_keeps_it()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router.On(ScriptedAgent.Planner, router.Tool(PlannerTools.CommitTasks, new
        {
            summary = "Ganti sapaan dan tambah Farewell.",
            tasks = new[] { Step("T1", GreeterFile, "Return \"Halo\" from Hello().") }
        }));
        router.On(ScriptedAgent.Planner, router.Tool(PlannerTools.AskUser, new
        {
            question = "Farewell perlu method Bye?",
            options = new[] { new { label = "Ya" }, new { label = "Tidak" } }
        }));
        router.On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
        {
            Summary = "Ganti sapaan dan tambah Farewell tanpa Bye.",
            Tasks = [Step("T2", FarewellFile, "Create a Farewell class.", "create", "T1")]
        }));
        router
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .OnExecutor(FarewellFile, ScriptedRouter.Create(Farewell, "Farewell dibuat."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));

        var (first, firstObserver) = await harness.RunAsync("Ganti sapaan dan tambah Farewell", ThreadSource.Telegram, "session-early");

        Assert.Equal(ThreadStatus.AwaitingInput, first.Status);
        // T1 is done before the thread waits, and the question says so.
        Assert.Contains(HalloLine, harness.ReadFile(GreeterFile));
        Assert.Contains("1 tugas yang sudah di-commit planner sudah dijalankan", firstObserver.Final);
        var waiting = await harness.LoadThreadAsync(first.ThreadId);
        Assert.Equal(ThreadTaskStatus.Succeeded, Assert.Single(waiting.Tasks).Status);

        var (second, secondObserver) = await harness.RunAsync("2", ThreadSource.Telegram, "session-early");

        Assert.Equal(first.ThreadId, second.ThreadId);
        Assert.Equal(ThreadStatus.Completed, second.Status);
        Assert.Contains(FarewellLine, harness.ReadFile(FarewellFile));
        Assert.Contains("2/2 tugas", secondObserver.Final);
        Assert.Single(router.CallsTo(ScriptedAgent.Executor), c => Messages(c).Contains(GreeterFile) && !Messages(c).Contains(FarewellFile));

        var thread = await harness.LoadThreadAsync(first.ThreadId);
        Assert.Equal(["T1", "T2"], thread.Tasks.OrderBy(t => t.Order).Select(t => t.TaskKey));
        Assert.All(thread.Tasks, t => Assert.Equal(ThreadTaskStatus.Succeeded, t.Status));
    }

    [Fact]
    public async Task Planner_is_offered_commit_tasks_with_its_rules()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router.On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Ok." }));

        await harness.RunAsync("Halo?");

        var call = router.CallsTo(ScriptedAgent.Planner)[0];
        Assert.Contains(PlannerTools.CommitTasks, call.ToolNames!);
        Assert.Contains("# Committing tasks early (commit_tasks)", call.System);
        Assert.Contains("AIT starts executing a committed batch right away", call.System);
    }
}
