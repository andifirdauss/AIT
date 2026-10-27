using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// run_command delegated to a tools node: it runs what needs no new permission and never pauses the thread; the
// planner keeps run_command for the rest.
public class ToolsNodeCommandTests
{
    [Fact]
    public void Run_command_is_delegable_and_shared_with_the_planner()
    {
        Assert.Contains(PlannerTools.RunCommand, PlannerTools.Delegable);
        Assert.True(PlannerTools.Offered(PlannerTools.RunCommand, canBuild: false, canGit: false, canCommand: true));
        Assert.False(PlannerTools.Offered(PlannerTools.RunCommand, canBuild: true, canGit: true, canCommand: false));

        var group = new ToolsNodeGroup("scout", new AiNode { Name = "Scout" }, [PlannerTools.RunCommand]);
        var planner = PlannerTools.All(groups: [group], canCommand: true).Select(Name).ToList();
        Assert.Single(planner, n => n == PlannerTools.RunCommand);
        Assert.Contains(PlannerTools.Investigate, planner);

        var toolsNode = PlannerTools.ForToolsNode([PlannerTools.RunCommand]);
        var definition = toolsNode.Single(t => Name(t) == PlannerTools.RunCommand)!["function"]!["description"]!.GetValue<string>();
        Assert.Contains("read-only", definition);
        Assert.Contains("report_findings", definition);

        var prompt = AgentPrompts.PlannerDelegation([group]);
        Assert.DoesNotContain("these tools are not yours", prompt);
        Assert.Contains("run_command is shared", prompt);
    }

    [Fact]
    public async Task A_planner_can_save_run_command_as_a_delegation()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var nodes = harness.Nodes;
        var scout = await nodes.CreateAsync(Save("Scout", NodeRole.ToolsNode));
        var planner = await nodes.CreateAsync(Save("P", NodeRole.Planner, [new(PlannerTools.RunCommand, scout.Id)]));

        var saved = (await nodes.GetAsync(planner.Id))!;
        Assert.Equal([PlannerTools.RunCommand], saved.ToolDelegations!.Select(d => d.Tool));
    }

    [Fact]
    public async Task A_tools_node_runs_read_only_commands_and_never_pauses_for_permission()
    {
        using var _ = Lang.Use("en");
        await using var harness = await CreateAsync(addNodes: false);
        var planner = await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        await harness.AddNodeAsync(NodeRole.Executor, "executor-model");
        await harness.AddNodeAsync(NodeRole.Wrapper, "wrapper-model");
        var scout = await harness.AddNodeAsync(NodeRole.ToolsNode, "scout-model");
        await DelegateAsync(harness, planner.Id, scout.Id, PlannerTools.RunCommand);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.Investigate, new { question = "Which git version is installed?" }))
            .On(ScriptedAgent.ToolsNode, router.Tool(PlannerTools.RunCommand, new { command = "git --version" }))
            .On(ScriptedAgent.ToolsNode, router.Tool(PlannerTools.RunCommand, new { command = "npm install" }))
            .On(ScriptedAgent.ToolsNode, router.Tool(PlannerTools.ReportFindings, new { findings = "git is installed; npm install needs permission." }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Answered.", Answer = "Git is installed." }));

        // A session that could answer: the planner may ask, the tools node still must not.
        var (result, _) = await harness.RunAsync("Which git version?", ThreadSource.ClaudeCode, "session-tools-cmd");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        var plannerCalls = router.CallsTo(ScriptedAgent.Planner);
        Assert.Contains(PlannerTools.RunCommand, plannerCalls[0].ToolNames!);
        Assert.Contains(PlannerTools.Investigate, plannerCalls[0].ToolNames!);

        var subCalls = router.CallsTo(ScriptedAgent.ToolsNode);
        Assert.Equal(3, subCalls.Count);
        Assert.Contains(PlannerTools.RunCommand, subCalls[0].ToolNames!);
        Assert.Contains("Exit code 0", LastToolResult(subCalls[1].Messages));
        Assert.Contains("a sub agent cannot ask for", LastToolResult(subCalls[2].Messages));
        Assert.Empty(await harness.Service<IApprovalService>().ListAsync(harness.ProjectId));
    }

    [Fact]
    public async Task A_tools_node_runs_a_command_the_developer_always_allowed()
    {
        using var _ = Lang.Use("en");
        await using var harness = await CreateAsync(addNodes: false);
        var planner = await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        await harness.AddNodeAsync(NodeRole.Executor, "executor-model");
        await harness.AddNodeAsync(NodeRole.Wrapper, "wrapper-model");
        var scout = await harness.AddNodeAsync(NodeRole.ToolsNode, "scout-model");
        await DelegateAsync(harness, planner.Id, scout.Id, PlannerTools.RunCommand);
        const string command = "git --version && git --version";
        await harness.Service<IApprovalService>().GrantAsync(harness.ProjectId, ApprovalKind.Command, command);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.Investigate, new { question = "Which git version is installed?" }))
            .On(ScriptedAgent.ToolsNode, router.Tool(PlannerTools.RunCommand, new { command }))
            .On(ScriptedAgent.ToolsNode, router.Tool(PlannerTools.ReportFindings, new { findings = "git is installed." }))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Answered.", Answer = "Git is installed." }));

        var (result, _) = await harness.RunAsync("Which git version?", ThreadSource.ClaudeCode, "session-tools-grant");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.Contains("Exit code 0", LastToolResult(router.CallsTo(ScriptedAgent.ToolsNode)[1].Messages));
        var grant = Assert.Single(await harness.Service<IApprovalService>().ListAsync(harness.ProjectId));
        // Granting counts as the first use; the tools node's run is the second.
        Assert.Equal(2, grant.UseCount);
    }

    private static async Task DelegateAsync(OrchestrationHarness harness, Guid plannerId, Guid toolsNodeId, params string[] tools)
    {
        await using var db = await harness.CreateDbAsync();
        foreach (var tool in tools) db.PlannerToolDelegations.Add(new PlannerToolDelegation { PlannerNodeId = plannerId, ToolsNodeId = toolsNodeId, Tool = tool });
        await db.SaveChangesAsync();
    }

    private static string LastToolResult(JsonArray messages) =>
        messages.OfType<JsonObject>().Last(m => m["role"]?.GetValue<string>() == "tool")["content"]!.GetValue<string>();

    private static string Name(JsonNode? tool) => tool!["function"]!["name"]!.GetValue<string>();

    private static SaveNodeRequest Save(string name, NodeRole role, IReadOnlyList<ToolDelegationRequest>? delegations = null) =>
        new(name, role, $"{name.ToLowerInvariant()}-model", null, 4096, 2, 100, null, true, null, null, delegations);
}
