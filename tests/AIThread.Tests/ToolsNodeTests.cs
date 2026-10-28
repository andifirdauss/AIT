using System.Text.Json.Nodes;
using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;
using static AIThread.Tests.ThreadOrchestratorTests;

namespace AIThread.Tests;

// Planner tools delegated to tools nodes (PlannerToolDelegation): the planner asks through investigate and only the
// tools node's findings enter its conversation.
public class ToolsNodeTests
{
    [Fact]
    public async Task Planner_asks_a_sub_node_for_delegated_tools_and_only_sees_its_findings()
    {
        await using var harness = await CreateAsync(addNodes: false);
        var planner = await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        await harness.AddNodeAsync(NodeRole.Executor, "executor-model");
        await harness.AddNodeAsync(NodeRole.Wrapper, "wrapper-model");
        var scout = await harness.AddNodeAsync(NodeRole.ToolsNode, "scout-model");
        await DelegateAsync(harness, planner.Id, scout.Id, PlannerTools.ReadFile, PlannerTools.SearchText);
        const string findings = "src/Greeter.cs:5-5 Hello returns \"Hi\".";
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.Investigate, new { question = "What does Greeter.Hello return?" }, tokens: 100))
            .On(ScriptedAgent.ToolsNode, router.Tool(PlannerTools.ReadFile, new { path = GreeterFile }, tokens: 7))
            .On(ScriptedAgent.ToolsNode, router.Tool(PlannerTools.ReportFindings, new { findings }, tokens: 3))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Hello mengembalikan Hi." }));

        var (result, observer) = await harness.RunAsync("Apa isi Hello?");

        Assert.Equal(ThreadStatus.Completed, result.Status);

        var plannerCalls = router.CallsTo(ScriptedAgent.Planner);
        Assert.DoesNotContain(PlannerTools.ReadFile, plannerCalls[0].ToolNames!);
        Assert.DoesNotContain(PlannerTools.SearchText, plannerCalls[0].ToolNames!);
        Assert.Contains(PlannerTools.FindSymbol, plannerCalls[0].ToolNames!);
        Assert.Contains(PlannerTools.Investigate, plannerCalls[0].ToolNames!);
        Assert.Contains("Delegated tools", plannerCalls[0].System);

        var subCalls = router.CallsTo(ScriptedAgent.ToolsNode);
        Assert.Equal(2, subCalls.Count);
        Assert.Equal("scout-model", subCalls[0].Model);
        // A sub node that reads files gets the skeleton tools too.
        Assert.Equal([PlannerTools.SearchText, PlannerTools.ReadFile, PlannerTools.CodeOutline, PlannerTools.FunctionCalls, PlannerTools.ReportFindings], subCalls[0].ToolNames);
        Assert.Contains("What does Greeter.Hello return?", subCalls[0].LastUser);
        Assert.Contains("Hello()", LastToolResult(subCalls[1].Messages));

        // The planner gets the findings as the result of investigate, not the file.
        Assert.Equal(findings, LastToolResult(plannerCalls[1].Messages));

        var thread = await harness.LoadThreadAsync(result.ThreadId);
        Assert.Equal(20, thread.ToolsNodeTokens);
        Assert.Equal(220, thread.PlannerTokens);
        Assert.Contains(observer.Lines, l => l.Contains("investigate") && l.Contains("#1"));
        Assert.Contains(observer.Lines, l => l.Contains("#1 › read_file"));
        Assert.Contains(observer.Lines, l => l.Contains("#1 › complete") && l.Contains("2 langkah"));
    }

    [Fact]
    public async Task Excerpts_a_sub_node_names_reach_the_planner_copied_from_the_file()
    {
        await using var harness = await CreateAsync(addNodes: false);
        var planner = await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        await harness.AddNodeAsync(NodeRole.Executor, "executor-model");
        await harness.AddNodeAsync(NodeRole.Wrapper, "wrapper-model");
        var scout = await harness.AddNodeAsync(NodeRole.ToolsNode, "scout-model");
        await DelegateAsync(harness, planner.Id, scout.Id, PlannerTools.ReadFile);
        // One range for the class, then more ranges than an investigation may attach (the duplicate counts once).
        var excerpts = new[] { new { path = GreeterFile, startLine = 3, endLine = 6 }, new { path = GreeterFile, startLine = 3, endLine = 6 } }
            .Concat(Enumerable.Range(1, 8).Select(i => new { path = GreeterFile, startLine = 1, endLine = i }))
            .ToArray();
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.Investigate, new { question = "Show class Greeter as exact text." }, tokens: 100))
            .On(ScriptedAgent.ToolsNode, router.Tool(PlannerTools.ReportFindings, new { findings = "src/Greeter.cs:3-6 class Greeter.", excerpts }, tokens: 3))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Greeter punya Hello." }));

        var (result, observer) = await harness.RunAsync("Tampilkan class Greeter.");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        var answer = LastToolResult(router.CallsTo(ScriptedAgent.Planner)[1].Messages);
        Assert.StartsWith("src/Greeter.cs:3-6 class Greeter.", answer);
        Assert.Contains("Exact excerpts, copied by AIT from the files:", answer);
        Assert.Contains("3| public class Greeter", answer);
        Assert.Contains("5|     public string Hello() => \"Hi\";", answer);
        Assert.Contains("1 more excerpt(s) left out", answer);
        // The tools node wrote no code: one turn, the lines came from the file.
        Assert.Single(router.CallsTo(ScriptedAgent.ToolsNode));
        Assert.Contains(observer.Lines, l => l.Contains("#1 › read_file") && l.Contains("3-6"));
    }

    [Fact]
    public async Task An_inactive_sub_node_gives_its_tools_back_to_the_planner()
    {
        await using var harness = await CreateAsync(addNodes: false);
        var planner = await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        await harness.AddNodeAsync(NodeRole.Executor, "executor-model");
        await harness.AddNodeAsync(NodeRole.Wrapper, "wrapper-model");
        var scout = await harness.AddNodeAsync(NodeRole.ToolsNode, "scout-model", isActive: false);
        await DelegateAsync(harness, planner.Id, scout.Id, PlannerTools.ReadFile);
        var router = harness.Router;
        router.On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Halo." }));

        var (result, _) = await harness.RunAsync("Halo?");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        var call = Assert.Single(router.CallsTo(ScriptedAgent.Planner));
        Assert.Contains(PlannerTools.ReadFile, call.ToolNames!);
        Assert.DoesNotContain(PlannerTools.Investigate, call.ToolNames!);
        Assert.DoesNotContain("Delegated tools", call.System);
    }

    [Fact]
    public async Task A_sub_node_that_stays_unavailable_returns_an_error_to_the_planner()
    {
        await using var harness = await CreateAsync(addNodes: false);
        var planner = await harness.AddNodeAsync(NodeRole.Planner, "planner-model");
        await harness.AddNodeAsync(NodeRole.Executor, "executor-model");
        await harness.AddNodeAsync(NodeRole.Wrapper, "wrapper-model");
        var scout = await harness.AddNodeAsync(NodeRole.ToolsNode, "scout-model");
        await DelegateAsync(harness, planner.Id, scout.Id, PlannerTools.ReadFile);
        var router = harness.Router;
        router
            .On(ScriptedAgent.Planner, router.Tool(PlannerTools.Investigate, new { question = "Read Greeter." }))
            .On(ScriptedAgent.ToolsNode, _ => throw new NineRouterException("Too many requests", 429))
            .On(ScriptedAgent.Planner, router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Tidak bisa membaca." }));

        var (result, _) = await harness.RunAsync("Apa isi Greeter?");

        Assert.Equal(ThreadStatus.Completed, result.Status);
        Assert.StartsWith("Error: the sub agent is unavailable", LastToolResult(router.CallsTo(ScriptedAgent.Planner)[1].Messages));
    }

    [Fact]
    public void Delegated_tools_leave_the_planner_list_and_agent_is_asked_only_with_several_sub_nodes()
    {
        var scout = new ToolsNodeGroup("scout", new AiNode { Name = "Scout" }, [PlannerTools.ReadFile, PlannerTools.SearchText]);
        var builder = new ToolsNodeGroup("builder", new AiNode { Name = "Builder" }, [PlannerTools.RunBuild]);

        var tools = PlannerTools.All(canBuild: true, canAsk: true, [scout, builder]);
        var names = tools.Select(Name).ToList();
        Assert.DoesNotContain(PlannerTools.ReadFile, names);
        Assert.DoesNotContain(PlannerTools.RunBuild, names);
        Assert.Contains(PlannerTools.FindSymbol, names);
        Assert.Equal([PlannerTools.Investigate, PlannerTools.AskUser, PlannerTools.SubmitPlan], names.TakeLast(3));
        var parameters = Parameters(tools, PlannerTools.Investigate);
        Assert.Equal(["scout", "builder"], parameters["properties"]!["agent"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()));

        var single = Parameters(PlannerTools.All(groups: [scout]), PlannerTools.Investigate);
        Assert.Null(single["properties"]!["agent"]);

        Assert.DoesNotContain(PlannerTools.Investigate, PlannerTools.All().Select(Name));
    }

    [Fact]
    public async Task Saving_delegations_checks_tools_and_targets_and_releases_them_when_the_sub_node_goes()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var nodes = harness.Nodes;
        var scout = await nodes.CreateAsync(Save("Scout", NodeRole.ToolsNode));
        var executor = await nodes.CreateAsync(Save("E", NodeRole.Executor));
        var planner = await nodes.CreateAsync(Save("P", NodeRole.Planner,
            [new(PlannerTools.SearchText, scout.Id), new(PlannerTools.ReadFile, scout.Id)]));

        var saved = (await nodes.GetAsync(planner.Id))!;
        Assert.Equal([PlannerTools.SearchText, PlannerTools.ReadFile], saved.ToolDelegations!.Select(d => d.Tool));
        Assert.All(saved.ToolDelegations!, d => Assert.Equal("Scout", d.ToolsNodeName));

        await Assert.ThrowsAsync<AppValidationException>(() => nodes.UpdateAsync(planner.Id, Save("P", NodeRole.Planner, [new(PlannerTools.SubmitPlan, scout.Id)])));
        var role = await Assert.ThrowsAsync<AppValidationException>(() => nodes.UpdateAsync(planner.Id, Save("P", NodeRole.Planner, [new(PlannerTools.ReadFile, executor.Id)])));
        Assert.Contains("ToolsNode", role.Message);

        // null leaves the delegations as they are.
        await nodes.UpdateAsync(planner.Id, Save("P", NodeRole.Planner));
        Assert.Equal(2, (await nodes.GetAsync(planner.Id))!.ToolDelegations!.Count);

        await nodes.UpdateAsync(scout.Id, Save("Scout", NodeRole.Executor));
        Assert.Empty((await nodes.GetAsync(planner.Id))!.ToolDelegations!);

        var other = await nodes.CreateAsync(Save("Other", NodeRole.ToolsNode));
        await nodes.UpdateAsync(planner.Id, Save("P", NodeRole.Planner, [new(PlannerTools.ReadFile, other.Id)]));
        Assert.True(await nodes.DeleteAsync(other.Id));
        Assert.Empty((await nodes.GetAsync(planner.Id))!.ToolDelegations!);

        // A planner that becomes another role drops its delegations.
        var third = await nodes.CreateAsync(Save("Third", NodeRole.ToolsNode));
        await nodes.UpdateAsync(planner.Id, Save("P", NodeRole.Planner, [new(PlannerTools.ReadFile, third.Id)]));
        await nodes.UpdateAsync(planner.Id, Save("P", NodeRole.Reviewer));
        Assert.Empty((await nodes.GetAsync(planner.Id))!.ToolDelegations!);
    }

    [Theory]
    [InlineData("src/Greeter.cs", "src/Greeter.cs")]
    [InlineData("KN2022_KCO_KDS.MVC/Scripts/customs/transactions/mkppdf/mkppscript.js", "KN2022_KCO_KDS.MVC/…/mkppdf/mkppscript.js")]
    [InlineData(@"D:\Work\Very\Long\Folder\Chain\That\Keeps\Going\Program.cs", "D:/…/Folder/Chain/That/Keeps/Going/Program.cs")]
    public void Long_paths_are_shortened_for_the_progress(string path, string expected)
    {
        var compact = ClientProgress.CompactPath(path, 48);
        Assert.Equal(expected, compact);
        Assert.True(compact.Length <= 48);
    }

    [Fact]
    public void A_tool_line_with_a_long_path_stays_on_one_line()
    {
        var args = new JsonObject { ["path"] = "KN2022_KCO_KDS.MVC/Scripts/customs/transactions/mkppdf/mkppscript.js", ["startLine"] = 1990, ["endLine"] = 2080 };
        var line = "#12 › " + ClientProgress.Pad(PlannerTools.ReadFile, PlannerTools.NameColumn) + PlannerToolRunner.Describe(PlannerTools.ReadFile, args, compact: true);

        var wrapped = ClientProgress.Wrap(line, "│  ├─ ", "│  │  ");

        Assert.Equal("│  ├─ #12 › read_file        KN2022_KCO_KDS.MVC/…/mkppdf/mkppscript.js (1990-2080)\n", wrapped);
        Assert.Contains("transactions", PlannerToolRunner.Describe(PlannerTools.ReadFile, args));
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

    private static JsonNode Parameters(JsonArray tools, string name) => tools.Single(t => Name(t) == name)!["function"]!["parameters"]!;

    private static SaveNodeRequest Save(string name, NodeRole role, IReadOnlyList<ToolDelegationRequest>? delegations = null) =>
        new(name, role, $"{name.ToLowerInvariant()}-model", null, 4096, 2, 100, null, true, null, null, delegations);
}
