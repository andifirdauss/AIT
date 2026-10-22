using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// "This change" in a follow-up: the brief lists what the chat's earlier threads changed per module, and asks the planner
// to let the developer choose when it spans several modules.
public class SessionChangesTests
{
    private static readonly DateTime Now = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Changes_are_grouped_per_module_newest_first()
    {
        var older = new SessionChangeThread(Guid.NewGuid(), "ubah sync outlet", Now.AddHours(-2), ["src/Outlet/Sync.cs", "src/Shared/Paging.cs"]);
        var newer = new SessionChangeThread(Guid.NewGuid(), "ubah dashboard", Now.AddMinutes(-12), ["src/Approval/Dashboard.cs", "src/Shared/Paging.cs"]);
        var modules = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["src/Outlet/Sync.cs"] = "OutletUnion",
            ["src/Approval/Dashboard.cs"] = "ApprovalHierarchy"
        };

        var changes = SessionChanges.ByModule([newer, older], modules);

        // A file without a module stays with its thread: one bucket for all of them would merge different features.
        var noModule = SessionChanges.UnmappedPrefix + " thread ";
        Assert.Equal(["ApprovalHierarchy", noModule + newer.ThreadId.ToString("N")[..8], "OutletUnion", noModule + older.ThreadId.ToString("N")[..8]],
            changes.Select(c => c.Module));

        var asked = SessionChanges.Brief(changes, canAsk: true, unitTests: null, Now);
        Assert.Contains($"- ApprovalHierarchy · thread {newer.ThreadId.ToString("N")[..8]} (12 min ago)", asked);
        Assert.Contains("(12 min ago) \"ubah dashboard\" · 1 file(s): src/Approval/Dashboard.cs", asked);
        Assert.Contains("never the whole git diff", asked);
        Assert.Contains("call ask_user before anything else", asked);

        var told = SessionChanges.Brief(changes, canAsk: false, unitTests: null, Now);
        Assert.DoesNotContain("ask_user", told);
        Assert.Contains("take the newest module and say so in the summary", told);

        // One module: nothing to choose.
        Assert.DoesNotContain("More than one module", SessionChanges.Brief(changes.Take(1).ToList(), canAsk: true, unitTests: null, Now));
    }

    [Fact]
    public async Task The_brief_lists_the_files_the_sessions_threads_changed_and_leaves_out_answers_and_rollbacks()
    {
        const string farewell = "namespace Demo;\n\npublic class Farewell\n{\n    public string Bye() => \"Bye\";\n}\n";
        await using var harness = await CreateAsync();
        harness.Project.Write(FarewellFile, farewell);
        var router = harness.Router;

        async Task<Guid> ChangeAsync(string file, string search, string replace)
        {
            router.On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = $"Ubah {file}.", Tasks = [Step("T1", file, "Change it.")] }))
                .OnExecutor(file, ScriptedRouter.Edit(search, replace, "Diubah."))
                .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));
            var (result, _) = await harness.RunAsync($"Ubah {file}");
            Assert.True(result.Status == ThreadStatus.Completed, file + ": " + (await harness.LoadThreadAsync(result.ThreadId)).Error);
            return result.ThreadId;
        }

        var greeter = await ChangeAsync(GreeterFile, HelloLine, HalloLine);
        var farewellChange = await ChangeAsync(FarewellFile, "\"Bye\"", "\"Dah\"");
        router.On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawaban.", Answer = "Greeter menyapa dengan Halo." }));
        var (answer, _) = await harness.RunAsync("Greeter menyapa apa?");
        var undone = await ChangeAsync(FarewellFile, "\"Dah\"", "\"Sampai jumpa\"");
        await harness.Service<IThreadRollback>().RollbackAsync(undone);

        router.On(ScriptedAgent.Planner, call =>
        {
            var brief = call.LastUser;
            Assert.Contains("# Changes in this session", brief);
            Assert.Contains(greeter.ToString("N")[..8], brief);
            Assert.Contains(farewellChange.ToString("N")[..8], brief);
            Assert.Contains(GreeterFile, brief);
            Assert.DoesNotContain(answer.ThreadId.ToString("N")[..8], brief);
            Assert.DoesNotContain(undone.ToString("N")[..8], brief);
            // Two modules and a chat to answer in: the planner asks which one.
            Assert.Contains("call ask_user before anything else", brief);
            return router.SubmitPlan(new ThreadPlan { Summary = "Jawaban.", Answer = "Oke." });
        });
        var request = new ThreadRequest(ThreadSource.ClaudeCode, "Kamu bisa jalankan test perubahan ini?", "Kamu bisa jalankan test perubahan ini?", null,
            harness.Project.Root, harness.ProjectId, "session-1", new SessionContext(null, null, [greeter, farewellChange, answer.ThreadId, undone]));
        var final = await harness.Orchestrator.RunAsync(request, new RecordingObserver(), CancellationToken.None);

        Assert.True(final.Status == ThreadStatus.Completed, (await harness.LoadThreadAsync(final.ThreadId)).Error);
        Assert.Single(router.CallsTo(ScriptedAgent.Planner).Where(c => c.LastUser.Contains("# Changes in this session")));
    }
}
