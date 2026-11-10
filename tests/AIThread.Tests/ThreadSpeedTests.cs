using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Data.Messaging;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using AIThread.Service.Verification;
using Microsoft.Extensions.DependencyInjection;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

public class ThreadSpeedTests
{
    [Fact]
    public async Task A_small_file_the_request_names_goes_in_the_brief_whole()
    {
        await using var harness = await CreateAsync();
        var router = harness.Router;
        router.On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Ok." }));

        await harness.RunAsync($"Rapikan {GreeterFile}");

        var brief = Assert.Single(router.CallsTo(ScriptedAgent.Planner)).LastUser;
        Assert.Contains("# Full text of the small files", brief);
        Assert.Contains($"| {HelloLine}", brief);
        // Its outline would only repeat what the brief already holds.
        Assert.DoesNotContain("# Outline of the files", brief);
    }

    [Fact]
    public async Task A_large_file_stays_out_of_the_brief()
    {
        const string bigFile = "src/Big.cs";
        await using var harness = await CreateAsync();
        harness.Project.Write(bigFile, string.Join('\n', Enumerable.Repeat("// a line that only takes up room", 250)));
        var router = harness.Router;
        router.On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Ok." }));

        await harness.RunAsync($"Rapikan {bigFile}");

        Assert.DoesNotContain("takes up room", Assert.Single(router.CallsTo(ScriptedAgent.Planner)).LastUser);
    }

    private const string Approved = """{"approved": true, "summary": "Sesuai.", "issues": []}""";

    private static async Task<OrchestrationHarness> WithBuildAsync(IBuildRunner build)
    {
        var harness = await OrchestrationHarness.CreateAsync(s => s.BuildEnabled = true, configureServices: s => s.AddSingleton(build));
        await harness.AddStandardNodesAsync();
        await harness.AddNodeAsync(NodeRole.Reviewer, "reviewer-model");
        harness.Project.Write("App.slnx", """<Solution><Project Path="src/App.csproj" /></Solution>""");
        harness.Project.Write("src/App.csproj", """<Project Sdk="Microsoft.NET.Sdk"></Project>""");
        harness.Project.Write(GreeterFile, Greeter);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Sapaan diganti.", Tasks = [Step("T1", GreeterFile, "Return \"Halo\" from Hello().")] }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."));
        return harness;
    }

    [Fact]
    public async Task The_reviewer_works_while_the_build_runs()
    {
        var reviewing = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var build = new ScriptedBuild(async () =>
        {
            // Sequential steps would end here: the reviewer would only start after this build.
            await reviewing.Task.WaitAsync(TimeSpan.FromSeconds(20));
            return true;
        });
        await using var harness = await WithBuildAsync(build);
        harness.Router.On(ScriptedAgent.Reviewer, _ =>
        {
            reviewing.TrySetResult();
            return ScriptedRouter.Text(Approved);
        });

        var (result, _) = await harness.RunAsync("Ganti sapaan jadi Halo");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Single(harness.Router.CallsTo(ScriptedAgent.Reviewer));
        Assert.Contains(VerificationReport.Passed, (await harness.LoadThreadAsync(result.ThreadId)).VerificationJson);
    }

    [Fact]
    public async Task A_build_that_fails_is_reviewed_again_with_its_result()
    {
        await using var harness = await WithBuildAsync(new ScriptedBuild(() => Task.FromResult(false)));
        harness.Router
            .On(ScriptedAgent.Reviewer, ScriptedRouter.Text(Approved))
            .On(ScriptedAgent.Reviewer, ScriptedRouter.Text(Approved));

        await harness.RunAsync("Ganti sapaan jadi Halo");

        var reviews = harness.Router.CallsTo(ScriptedAgent.Reviewer);
        Assert.Equal(2, reviews.Count);
        Assert.NotEqual(reviews[0].LastUser, reviews[1].LastUser);
    }

    private sealed class ScriptedBuild(Func<Task<bool>> run) : IBuildRunner
    {
        public async Task<BuildResult> RunAsync(BuildCommand command, TimeSpan timeout, CancellationToken cancellationToken) =>
            new(command.Display, await run(), false, 0, [], 0, string.Empty, 1);
    }

    [Fact]
    public async Task Silent_mode_sends_no_memory_notification()
    {
        await using var harness = await OrchestrationHarness.CreateAsync(s => s.SilentNotifications = true);
        await harness.AddStandardNodesAsync();
        harness.Project.Write(GreeterFile, Greeter);
        using var events = harness.Service<IEventBus>().Subscribe(e => e.Type == AppEventTypes.MemoryExtracted);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Sapaan diganti.", Tasks = [Step("T1", GreeterFile, "Return \"Halo\" from Hello().")] }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."))
            .On(ScriptedAgent.MemoryExtractor, ScriptedRouter.Text("""
                {"entry": {"title": "Sapaan jadi Halo", "module": "greeter", "other_modules": [], "feature": "Sapaan memakai `Halo`.",
                  "files": [{"path": "src/Greeter.cs", "change": "`Hello()` mengembalikan `Halo`"}],
                  "validation": [], "flow": [], "notes": [], "keywords": ["sapaan", "Greeter.Hello", "halo"]}}
                """));

        var (_, observer) = await harness.RunAsync("Ganti sapaan jadi Halo");

        // The harness waits for the extraction, so the memory is saved by now and no event went out for it.
        Assert.Contains("+1 memory baru", observer.Final);
        Assert.False(events.Reader.TryRead(out _));
    }

    [Fact]
    public async Task Silent_mode_is_saved_with_the_settings()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var settings = harness.Service<ISettingService>();
        var now = await settings.GetAsync(string.Empty, CancellationToken.None);
        Assert.False(now.SilentNotifications);

        await settings.UpdateAsync(new UpdateSystemSettingRequest(now.NineRouterBaseUrl, null, false, now.ProxyApiKey, now.PassthroughModel, now.OrchestrationEnabled,
            now.StreamProgressToClient, now.PlannerMaxSteps, now.ExecutorMaxRetries, now.MaxParallelTasks, now.RequestTimeoutSeconds, SilentNotifications: true), string.Empty, CancellationToken.None);

        Assert.True((await settings.GetAsync(string.Empty, CancellationToken.None)).SilentNotifications);
        Assert.True((await settings.GetRuntimeAsync(CancellationToken.None)).SilentNotifications);
    }

    [Fact]
    public async Task The_dashboard_is_told_when_the_memory_extraction_saved_something()
    {
        await using var harness = await CreateAsync();
        using var events = harness.Service<IEventBus>().Subscribe(e => e.Type == AppEventTypes.MemoryExtracted);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Sapaan diganti.", Tasks = [Step("T1", GreeterFile, "Return \"Halo\" from Hello().")] }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Selesai."))
            .On(ScriptedAgent.MemoryExtractor, ScriptedRouter.Text("""
                {"entry": {"title": "Sapaan jadi Halo", "module": "greeter", "other_modules": [], "feature": "Sapaan memakai `Halo`.",
                  "files": [{"path": "src/Greeter.cs", "change": "`Hello()` mengembalikan `Halo`"}],
                  "validation": [], "flow": [], "notes": [], "keywords": ["sapaan", "Greeter.Hello", "halo"]}}
                """));

        var (result, _) = await harness.RunAsync("Ganti sapaan jadi Halo");

        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var notice = await events.Reader.ReadAsync(limit.Token);
        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Equal(result.ThreadId, notice.ThreadId);
    }
}
