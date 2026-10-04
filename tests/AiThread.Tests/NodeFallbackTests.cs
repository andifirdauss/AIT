using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using static AiThread.Tests.ThreadOrchestratorTests;

namespace AiThread.Tests;

// AiNode.FallbackNodeId: an unavailable node hands the same call to the fallback chosen for it, and only to that one.
public class NodeFallbackTests
{
    [Fact]
    public async Task Without_a_fallback_a_rate_limited_planner_fails_the_thread_even_with_other_planners_active()
    {
        await using var harness = await CreateAsync(addNodes: false);
        await harness.AddNodeAsync(NodeRole.Planner, "planner-main", priority: 1);
        await harness.AddNodeAsync(NodeRole.Planner, "planner-other", priority: 2);
        harness.Router.On(ScriptedAgent.Planner, _ => throw new NineRouterException("Too many requests", 429));

        var (result, _) = await harness.RunAsync("Halo?");

        Assert.Equal(ThreadStatus.Failed, result.Status);
        Assert.Equal(["planner-main"], harness.Router.CallsTo(ScriptedAgent.Planner).Select(c => c.Model));
        Assert.Contains("Too many requests", (await harness.LoadThreadAsync(result.ThreadId)).Error);
    }

    [Fact]
    public async Task Fallback_chain_is_followed_until_a_node_answers()
    {
        await using var harness = await CreateAsync(addNodes: false);
        var third = await harness.AddNodeAsync(NodeRole.Planner, "planner-3", isActive: false);
        var second = await harness.AddNodeAsync(NodeRole.Planner, "planner-2", isActive: false, fallbackNodeId: third.Id);
        await harness.AddNodeAsync(NodeRole.Planner, "planner-1", fallbackNodeId: second.Id);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, _ => throw new NineRouterException("Overloaded", 529))
            .On(ScriptedAgent.Planner, _ => throw new NineRouterException("Timeout", 408))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Dijawab node ketiga." }));

        var (result, observer) = await harness.RunAsync("Halo?");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Equal(["planner-1", "planner-2", "planner-3"], router.CallsTo(ScriptedAgent.Planner).Select(c => c.Model));
        Assert.Contains(observer.Lines, l => l.Contains("beralih ke `planner-2`"));
        Assert.Contains(observer.Lines, l => l.Contains("beralih ke `planner-3`"));
        Assert.Equal("planner-3", (await harness.LoadThreadAsync(result.ThreadId)).PlannerModel);
    }

    [Fact]
    public async Task Executor_hands_its_task_to_an_inactive_backup_node()
    {
        await using var harness = await CreateAsync(addNodes: false);
        await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        var backup = await harness.AddNodeAsync(NodeRole.Executor, "executor-backup", isActive: false);
        await harness.AddNodeAsync(NodeRole.Executor, "executor-main", fallbackNodeId: backup.Id);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Sapaan jadi Halo.",
                Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")]
            }))
            .OnExecutor(GreeterFile, _ => throw new NineRouterException("Too many requests", 429))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."));

        var (result, observer) = await harness.RunAsync("Sapaan jadi Halo");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains(HalloLine, harness.ReadFile(GreeterFile));
        Assert.Equal(["executor-main", "executor-backup"], router.CallsTo(ScriptedAgent.Executor).Select(c => c.Model));
        Assert.Contains(observer.Lines, l => l.Contains("Executor T1 `executor-main` tidak tersedia (rate limit 429), beralih ke `executor-backup`"));

        var task = Assert.Single((await harness.LoadThreadAsync(result.ThreadId)).Tasks);
        Assert.Equal(ThreadTaskStatus.Succeeded, task.Status);
        Assert.Equal("executor-backup", task.Model);
        Assert.Equal(backup.Id, task.NodeId);
    }

    [Fact]
    public async Task Wrapper_and_reviewer_fall_back_too()
    {
        await using var harness = await CreateAsync(addNodes: false);
        await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        await harness.AddNodeAsync(NodeRole.Executor, "executor-model");
        var wrapperBackup = await harness.AddNodeAsync(NodeRole.Wrapper, "wrapper-backup", isActive: false);
        await harness.AddNodeAsync(NodeRole.Wrapper, "wrapper-main", fallbackNodeId: wrapperBackup.Id);
        var reviewerBackup = await harness.AddNodeAsync(NodeRole.Reviewer, "reviewer-backup", isActive: false);
        await harness.AddNodeAsync(NodeRole.Reviewer, "reviewer-main", fallbackNodeId: reviewerBackup.Id);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan
            {
                Summary = "Sapaan jadi Halo.",
                Tasks = [Step("T1", GreeterFile, "Return \"Halo\".")]
            }))
            .OnExecutor(GreeterFile, ScriptedRouter.Edit(HelloLine, HalloLine, "Sapaan diganti."))
            .On(ScriptedAgent.Reviewer, _ => throw new NineRouterException("Service unavailable", 503))
            .On(ScriptedAgent.Reviewer, ScriptedRouter.Text("""{"approved": true, "summary": "Oke.", "issues": []}"""))
            .On(ScriptedAgent.Wrapper, _ => throw new NineRouterException("Bad gateway", 502))
            .On(ScriptedAgent.Wrapper, ScriptedRouter.Text("Ringkasan dari wrapper cadangan."));

        var (result, observer) = await harness.RunAsync("Sapaan jadi Halo");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.EndsWith("Ringkasan dari wrapper cadangan.", observer.Final);
        Assert.Equal(["reviewer-main", "reviewer-backup"], router.CallsTo(ScriptedAgent.Reviewer).Select(c => c.Model));

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal("wrapper-backup", thread.WrapperModel);
        Assert.Equal("Ringkasan dari wrapper cadangan.", thread.FinalResponse);
        Assert.Contains("\"outcome\": \"passed\"", thread.VerificationJson);
    }

    [Fact]
    public async Task Errors_that_are_not_about_availability_do_not_switch_nodes()
    {
        await using var harness = await CreateAsync(addNodes: false);
        var backup = await harness.AddNodeAsync(NodeRole.Planner, "planner-backup", isActive: false);
        await harness.AddNodeAsync(NodeRole.Planner, "planner-main", fallbackNodeId: backup.Id);
        harness.Router.On(ScriptedAgent.Planner, _ => throw new NineRouterException("Invalid request", 400));

        var (result, _) = await harness.RunAsync("Halo?");

        Assert.Equal(ThreadStatus.Failed, result.Status);
        Assert.Single(harness.Router.CallsTo(ScriptedAgent.Planner));
    }

    [Fact]
    public async Task Saving_a_node_rejects_cycles_other_roles_and_itself()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var nodes = harness.Nodes;
        var a = await nodes.CreateAsync(Save("A", NodeRole.Executor));
        var b = await nodes.CreateAsync(Save("B", NodeRole.Executor, a.Id));
        var c = await nodes.CreateAsync(Save("C", NodeRole.Executor, b.Id));
        var wrapper = await nodes.CreateAsync(Save("W", NodeRole.Wrapper));

        Assert.Equal("A", b.FallbackNodeName);

        var cycle = await Assert.ThrowsAsync<AppValidationException>(() => nodes.UpdateAsync(a.Id, Save("A", NodeRole.Executor, c.Id)));
        Assert.Contains("berputar", cycle.Message);
        var role = await Assert.ThrowsAsync<AppValidationException>(() => nodes.UpdateAsync(a.Id, Save("A", NodeRole.Executor, wrapper.Id)));
        Assert.Contains("role", role.Message);
        await Assert.ThrowsAsync<AppValidationException>(() => nodes.UpdateAsync(a.Id, Save("A", NodeRole.Executor, a.Id)));
    }

    [Fact]
    public async Task Deleting_or_moving_a_backup_clears_the_references_to_it()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var nodes = harness.Nodes;
        var a = await nodes.CreateAsync(Save("A", NodeRole.Executor));
        var b = await nodes.CreateAsync(Save("B", NodeRole.Executor, a.Id));
        var c = await nodes.CreateAsync(Save("C", NodeRole.Executor));
        var d = await nodes.CreateAsync(Save("D", NodeRole.Executor, c.Id));

        Assert.True(await nodes.DeleteAsync(a.Id));
        Assert.Null((await nodes.GetAsync(b.Id))!.FallbackNodeId);

        await nodes.UpdateAsync(c.Id, Save("C", NodeRole.Reviewer));
        Assert.Null((await nodes.GetAsync(d.Id))!.FallbackNodeId);
    }

    private static SaveNodeRequest Save(string name, NodeRole role, Guid? fallbackNodeId = null) =>
        new(name, role, $"{name.ToLowerInvariant()}-model", null, 4096, 2, 100, null, true, null, fallbackNodeId);
}
