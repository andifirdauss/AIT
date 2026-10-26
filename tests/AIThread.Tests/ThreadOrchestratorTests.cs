using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;

namespace AIThread.Tests;

// End-to-end runs of ThreadOrchestrator: planner → parallel executors → verification → wrapper → memory, against the
// real services, a SQLite database and a real project folder; only the models are scripted (ScriptedRouter).
public class ThreadOrchestratorTests
{
    internal const string GreeterFile = "src/Greeter.cs";
    internal const string FarewellFile = "src/Farewell.cs";

    internal const string Greeter = """
        namespace Demo;

        public class Greeter
        {
            public string Hello() => "Hi";
        }

        """;

    internal const string HelloLine = """    public string Hello() => "Hi";""";
    internal const string HalloLine = """    public string Hello() => "Halo";""";

    [Fact]
    public async Task Plans_executes_dependent_tasks_wraps_and_extracts_memory()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.ReadFile, new { path = GreeterFile }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Ganti sapaan dan tambah Farewell.",
                Contract = [new ContractItem { Name = "Greeting", Value = "Halo" }],
                Tasks =
                [
                    Step("T1", GreeterFile, "Return \"Halo\" from Hello()."),
                    Step("T2", FarewellFile, "Create a Farewell class.", "create", "T1")
                ]
            }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .OnExecutor(FarewellFile, ScriptedRouter.Create("namespace Demo;\n\npublic class Farewell { }", "Farewell dibuat."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai: sapaan jadi Halo dan Farewell ditambahkan."))
            .On(ScriptedAgent.MemoryExtractor, ScriptedRouter.Text("""
                {"entry": {"title": "Sapaan jadi Halo, class Farewell", "module": "greeter", "other_modules": [], "feature": "Sapaan memakai `Halo`.",
                  "files": [{"path": "src/Greeter.cs", "change": "`Hello()` mengembalikan `Halo`"}, {"path": "src/Other.cs", "change": "tidak diubah thread ini"}],
                  "validation": [], "flow": [], "notes": ["Belum ada test untuk `Farewell`."], "keywords": ["sapaan", "Greeter.Hello", "halo"]}}
                """));

        var (result, observer) = await harness.RunAsync("Ganti sapaan jadi Halo dan tambah class Farewell");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains(HalloLine, harness.ReadFile(GreeterFile));
        Assert.Contains("public class Farewell", harness.ReadFile(FarewellFile));
        // Progress is a stepper inside a ````text block; the answer is status, wrapper text, changed files, footer.
        Assert.StartsWith("````text\n🧠 Planner · planner-model\n", observer.Lines[1]);
        Assert.StartsWith("````\n\n---\n\n✅ **Selesai** · 2/2 tugas\n\n", observer.Final);
        Assert.Contains("Selesai: sapaan jadi Halo dan Farewell ditambahkan.", observer.Final);
        Assert.Contains("| `src/Greeter.cs` | ubah | +1 −1 | T1 |", observer.Final);
        Assert.Contains("| `src/Farewell.cs` | baru | +3 | T2 |", observer.Final);
        Assert.Contains("+1 memory baru", observer.Final);

        // The planner saw the file it asked for before submitting.
        var plannerCalls = router.CallsTo(ScriptedAgent.Planner);
        Assert.Equal(2, plannerCalls.Count);
        var readResult = plannerCalls[1].Messages.OfType<JsonObject>().Single(m => m["role"]?.GetValue<string>() == "tool");
        Assert.Contains(HelloLine, readResult["content"]!.GetValue<string>());

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal(ThreadStatus.Completed, thread.Status);
        Assert.Equal("Selesai: sapaan jadi Halo dan Farewell ditambahkan.", thread.FinalResponse);
        Assert.Equal(2, thread.PlannerSteps);
        Assert.Equal(40, thread.PlannerTokens);
        Assert.Equal(40, thread.ExecutorTokens);
        Assert.Equal(20, thread.WrapperTokens);
        Assert.Equal(100, result.TotalTokens);
        Assert.Contains("Halo", thread.PlanJson);

        var tasks = thread.Tasks.OrderBy(t => t.Order).ToList();
        Assert.Equal(["T1", "T2"], tasks.Select(t => t.TaskKey));
        Assert.All(tasks, t =>
        {
            Assert.Equal(ThreadTaskStatus.Succeeded, t.Status);
            Assert.False(string.IsNullOrWhiteSpace(t.Diff));
            Assert.Equal("executor-model", t.Model);
        });
        Assert.Contains("+" + HalloLine, tasks[0].Diff);
        Assert.True(tasks[1].StartedAt >= tasks[0].CompletedAt, "T2 depends on T1 and must start after it finished.");

        await harness.Memory.WaitForSaveAsync(TimeSpan.FromSeconds(10));
        var (memory, sourceThread) = Assert.Single(harness.Memory.Saved);
        Assert.Equal("greeter", memory.Module);
        Assert.Equal(MemoryKind.Change, memory.Kind);
        Assert.Contains("halo", memory.Keywords);
        // The file list is the files the thread changed, with the model's note where it gave one; a file it named that
        // the thread did not change is dropped.
        Assert.StartsWith("### Sapaan jadi Halo, class Farewell\n**Fitur:** Sapaan memakai `Halo`.\n", memory.Description);
        Assert.Contains("- `src/Farewell.cs` (baru)\n", memory.Description);
        Assert.Contains("- `src/Greeter.cs` (ubah) — `Hello()` mengembalikan `Halo`\n", memory.Description);
        Assert.DoesNotContain("src/Other.cs", memory.Description);
        Assert.Contains("**Catatan:**\n- Belum ada test untuk `Farewell`.", memory.Description);
        var extractorInput = router.CallsTo(ScriptedAgent.MemoryExtractor).Single().Messages.OfType<JsonObject>().Last()["content"]!.GetValue<string>();
        Assert.Contains("# Changed files\n- src/Farewell.cs (baru)\n- src/Greeter.cs (ubah)\n", extractorInput);
        Assert.Equal(harness.ProjectId, memory.ProjectId);
        Assert.Equal(result.ThreadId, sourceThread);
    }

    [Fact]
    public async Task Plan_without_tasks_is_answered_directly()
    {
        await using var harness = await CreateAsync();
        harness.Router.On(ScriptedAgent.Planner, harness.Router.SubmitPlan(new ThreadPlan
        {
            Summary = "Pertanyaan saja.",
            Answer = "Greeter.Hello() mengembalikan \"Hi\"."
        }));

        var (result, observer) = await harness.RunAsync("Apa yang dikembalikan Greeter.Hello()?");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains("````\n\n---\n\nGreeter.Hello() mengembalikan \"Hi\".", observer.Final);
        Assert.DoesNotContain("**File berubah**", observer.Final);
        Assert.Contains("🪙", observer.Final);
        Assert.Empty(harness.Router.CallsTo(ScriptedAgent.Executor));
        Assert.Empty(harness.Router.CallsTo(ScriptedAgent.Wrapper));

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal("Greeter.Hello() mengembalikan \"Hi\".", thread.FinalResponse);
        Assert.Empty(thread.Tasks);
        Assert.Equal(Greeter.ReplaceLineEndings("\n"), harness.ReadFile(GreeterFile));
    }

    [Fact]
    public async Task Reviewer_issue_becomes_a_fix_task_until_approved()
    {
        await using var harness = await CreateAsync();
        await harness.AddNodeAsync(NodeRole.Reviewer, "reviewer-model");
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Sapaan jadi Halo.",
                Tasks = [Step("T1", GreeterFile, "Return \"Halo\" from Hello().")]
            }))
            // The executor forgets the exclamation mark the developer asked for; the reviewer catches it.
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Reviewer, ScriptedRouter.Text("""
                {"approved": false, "summary": "Tanda seru hilang.", "issues": [
                  {"file": "src/Greeter.cs", "anchor": "Hello()", "problem": "Missing exclamation mark.", "fix": "Return \"Halo!\"."}
                ]}
                """))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HalloLine, """    public string Hello() => "Halo!";""", "Tanda seru ditambahkan."))
            .On(ScriptedAgent.Reviewer, ScriptedRouter.Text("""{"approved": true, "summary": "Sesuai permintaan.", "issues": []}"""))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Sapaan sekarang \"Halo!\"."));

        var (result, _) = await harness.RunAsync("Sapaan jadi \"Halo!\"");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains("""    public string Hello() => "Halo!";""", harness.ReadFile(GreeterFile));

        // The fix executor is told what the reviewer found.
        var fixCall = harness.Router.CallsTo(ScriptedAgent.Executor)[1];
        Assert.Contains("Missing exclamation mark.", fixCall.LastUser);

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal("reviewer-model", thread.ReviewerModel);
        Assert.Equal(40, thread.ReviewerTokens);
        Assert.Equal(40, thread.ExecutorTokens);
        Assert.Null(thread.Error);

        var tasks = thread.Tasks.OrderBy(t => t.Order).ToList();
        Assert.Equal(2, tasks.Count);
        Assert.Equal("T1", tasks[0].TaskKey);
        Assert.StartsWith("V1-", tasks[1].TaskKey);
        Assert.All(tasks, t => Assert.Equal(ThreadTaskStatus.Succeeded, t.Status));

        var report = JsonNode.Parse(thread.VerificationJson!)!;
        Assert.Equal(VerificationReport.Passed, report["outcome"]!.GetValue<string>());
        Assert.Equal(2, report["rounds"]!.AsArray().Count);
    }

    [Fact]
    public async Task Failed_task_is_retried_then_its_dependents_are_skipped()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        const string missing = "    public string Goodbye() => \"Bye\";";
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Ubah Greeter lalu buat Farewell.",
                Tasks =
                [
                    Step("T1", GreeterFile, "Rename the greeting."),
                    Step("T2", FarewellFile, "Create a Farewell class.", "create", "T1")
                ]
            }))
            // Both attempts search for text that is not in the file.
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(missing, HalloLine, "x"))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(missing, HalloLine, "x"))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Tidak ada perubahan yang berhasil."));

        var (result, _) = await harness.RunAsync("Ubah Greeter");

        Assert.Equal(ThreadStatus.Failed, result.Status);
        Assert.Equal(Greeter.ReplaceLineEndings("\n"), harness.ReadFile(GreeterFile));
        Assert.False(File.Exists(Path.Combine(harness.Project.Root, "src", "Farewell.cs")));

        // The retry carries the failure and the current file content back to the executor.
        var executorCalls = harness.Router.CallsTo(ScriptedAgent.Executor);
        Assert.Equal(2, executorCalls.Count);
        Assert.Contains("could not be applied", executorCalls[1].LastUser);

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Contains("2 tugas tidak berhasil", thread.Error);
        var tasks = thread.Tasks.OrderBy(t => t.Order).ToList();
        Assert.Equal(ThreadTaskStatus.Failed, tasks[0].Status);
        Assert.Equal(2, tasks[0].Attempts);
        Assert.Equal(ThreadTaskStatus.Skipped, tasks[1].Status);
        Assert.Contains("T1", tasks[1].Error);
        Assert.Null(thread.VerificationJson);
    }

    [Fact]
    public async Task Rate_limited_planner_hands_over_to_its_fallback_node()
    {
        await using var harness = await CreateAsync(addNodes: false);
        var backup = await harness.AddNodeAsync(NodeRole.Planner, "planner-backup", priority: 2);
        await harness.AddNodeAsync(NodeRole.Planner, "planner-main", priority: 1, fallbackNodeId: backup.Id);
        await harness.AddNodeAsync(NodeRole.Executor, "executor-model");
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, _ => throw new NineRouterException("Too many requests", 429))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Dijawab cadangan." }));

        var (result, observer) = await harness.RunAsync("Halo?");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Equal(["planner-main", "planner-backup"], router.CallsTo(ScriptedAgent.Planner).Select(c => c.Model));
        Assert.Contains(observer.Lines, l => l.Contains("→ pindah ke planner-backup"));

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal("planner-backup", thread.PlannerModel);
    }

    [Fact]
    public async Task Planner_question_pauses_the_thread_and_the_next_prompt_resumes_it()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.AskUser, new
            {
                question = "Sapaan mana yang dipakai?",
                options = new[] { new { label = "Halo" }, new { label = "Hai" } }
            }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Dijawab.", Answer = "Oke, memakai Halo." }));

        var (first, firstObserver) = await harness.RunAsync("Ganti sapaannya", ThreadSource.Telegram, "session-1");

        Assert.Equal(ThreadStatus.AwaitingInput, first.Status);
        Assert.Contains("Sapaan mana yang dipakai?", firstObserver.Final);
        Assert.Equal(ThreadStatus.AwaitingInput, (await harness.LoadThreadAsync(first.ThreadId)).Status);

        var (second, secondObserver) = await harness.RunAsync("1", ThreadSource.Telegram, "session-1");

        Assert.Equal(first.ThreadId, second.ThreadId);
        Assert.Equal(ThreadStatus.Completed, second.Status);
        Assert.Contains("Oke, memakai Halo.", secondObserver.Final);

        // The answer reaches the planner as the result of its ask_user call.
        var resumed = router.CallsTo(ScriptedAgent.Planner)[1].Messages;
        var toolMessage = resumed.OfType<JsonObject>().Last(m => m["role"]?.GetValue<string>() == "tool");
        Assert.Contains("option 1: Halo", toolMessage["content"]!.GetValue<string>());

        var thread = await harness.LoadThreadAsync(first.ThreadId);
        Assert.Equal(ThreadStatus.Completed, thread.Status);
        Assert.Equal(2, thread.PlannerSteps);
        Assert.Equal("Ganti sapaannya", thread.Prompt);
    }

    [Fact]
    public async Task Failing_wrapper_falls_back_to_the_automatic_summary()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Sapaan jadi Halo.",
                Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")]
            }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, _ => throw new NineRouterException("Bad gateway", 502));

        var (result, observer) = await harness.RunAsync("Sapaan jadi Halo");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains("Sapaan jadi Halo.", observer.Final);
        Assert.Contains(GreeterFile, observer.Final);

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.StartsWith("**Ringkasan:**", thread.FinalResponse);
        // The client gets the plan summary between the status and the files instead of the stored automatic summary.
        Assert.DoesNotContain("**Ringkasan:**", observer.Final);
        Assert.Contains("| `src/Greeter.cs` | ubah | +1 −1 | T1 |", observer.Final);
        Assert.Equal(0, thread.WrapperTokens);
    }

    [Fact]
    public async Task Plan_with_tasks_fails_without_an_executor_node()
    {
        await using var harness = await CreateAsync(addNodes: false);
        await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        harness.Router.On(ScriptedAgent.Planner, harness.Router.SubmitPlan(new ThreadPlan
        {
            Summary = "Sapaan jadi Halo.",
            Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")]
        }));

        var (result, observer) = await harness.RunAsync("Sapaan jadi Halo");

        Assert.Equal(ThreadStatus.Failed, result.Status);
        Assert.Contains("Executor", observer.Final);
        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Contains("Executor", thread.Error);
        Assert.Empty(thread.Tasks);
    }

    [Fact]
    public async Task Missing_planner_node_fails_the_thread()
    {
        await using var harness = await CreateAsync(addNodes: false);

        var (result, _) = await harness.RunAsync("Apa saja");

        Assert.Equal(ThreadStatus.Failed, result.Status);
        Assert.Contains("Planner", (await harness.LoadThreadAsync(result.ThreadId)).Error);
        Assert.Empty(harness.Router.Calls);
    }

    [Fact]
    public async Task Cancellation_during_planning_marks_the_thread_cancelled()
    {
        await using var harness = await CreateAsync();
        using var cancellation = new CancellationTokenSource();
        harness.Router.On(ScriptedAgent.Planner, _ =>
        {
            cancellation.Cancel();
            return harness.Router.Tool(PlannerTools.ReadFile, new { path = GreeterFile });
        });

        var (result, _) = await harness.RunAsync("Ubah Greeter", cancellationToken: cancellation.Token);

        Assert.Equal(ThreadStatus.Cancelled, result.Status);
        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal(ThreadStatus.Cancelled, thread.Status);
        Assert.NotNull(thread.CompletedAt);
    }

    internal static async Task<OrchestrationHarness> CreateAsync(bool addNodes = true)
    {
        var harness = await OrchestrationHarness.CreateAsync();
        harness.Project.Write(GreeterFile, Greeter);
        if (addNodes) await harness.AddStandardNodesAsync();
        return harness;
    }

    internal static PlanTask Step(string id, string file, string instruction, string action = "edit", params string[] dependsOn) => new()
    {
        Id = id,
        Title = instruction,
        File = file,
        Action = action,
        Instruction = instruction,
        DependsOn = dependsOn.Length == 0 ? null : dependsOn.ToList()
    };
}
