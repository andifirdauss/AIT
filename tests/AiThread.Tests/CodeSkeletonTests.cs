using AiThread.Common.Enums;
using AiThread.Service.CodeCalls;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Workspace;

namespace AiThread.Tests;

// code_outline and function_calls: the planner sees the skeleton of the code (declarations with line ranges, recorded
// calls) before it reads a file, and the files a request names are outlined in its brief.
public class CodeSkeletonTests
{
    private const string Script = """
        function p_InitForm() {
            p_InitiateDetail();
            p_initiateData();
        }

        function p_initiateData() {
            $.ajax({ type: "POST", url: "/Transaction/KlaimPending/InitiateData" });
        }

        function p_InitiateDetail() {
        }
        """;

    [Fact]
    public async Task A_file_outline_has_every_declaration_with_its_line_range()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        harness.Project.Write("Logic/OrderBL.cs", """
            namespace Shop.Logic
            {
                public partial class OrderBL
                {
                    public string Name { get; set; }

                    public void Save(int id)
                    {
                        Validate(id);
                    }
                }
            }
            """);
        var (project, scope) = await ContextAsync(harness);

        var outline = await Tools(harness).OutlineAsync(project, scope, "Logic/OrderBL.cs", null, default);

        Assert.StartsWith("Logic/OrderBL.cs (C#, 12 lines)\nnamespace Shop.Logic\nclass OrderBL (partial) 3-11\n", outline);
        Assert.Contains("  5 property", outline);
        Assert.Contains("  7-10 method void Save(int id)", outline);

        harness.Project.Write("Views/Index.cshtml", "<div></div>\n");
        Assert.StartsWith("Error: no outline", await Tools(harness).OutlineAsync(project, scope, "Views/Index.cshtml", null, default));
    }

    [Fact]
    public async Task A_class_is_found_by_name_in_every_file_of_a_partial_class_and_a_folder_lists_its_files()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        harness.Project.Write("Logic/OrderBL.cs", "public partial class OrderBL {\n    public void Save() {\n    }\n}\n");
        harness.Project.Write("Logic/OrderBL.Print.cs", "public partial class OrderBL {\n    public void Print() {\n    }\n}\n");
        harness.Project.Write("Logic/UserBL.cs", "public class UserBL {\n    public void Load() {\n    }\n}\n");
        harness.Project.Write("Scripts/order.js", Script);
        var (project, scope) = await ContextAsync(harness);
        var tools = Tools(harness);

        var order = await tools.OutlineAsync(project, scope, null, "OrderBL", default);
        Assert.Contains("Logic/OrderBL.cs (C#", order);
        Assert.Contains("Logic/OrderBL.Print.cs (C#", order);
        Assert.Contains("void Print()", order);
        Assert.DoesNotContain("UserBL", order);
        Assert.StartsWith("No type named 'Missing'", await tools.OutlineAsync(project, scope, null, "Missing", default));

        var folder = await tools.OutlineAsync(project, scope, "Logic", null, default);
        Assert.Contains("- Logic/UserBL.cs: class UserBL (1)", folder);
        Assert.DoesNotContain("order.js", folder);
        Assert.Contains("- Scripts/order.js: functions p_InitForm, p_initiateData, p_InitiateDetail", await tools.OutlineAsync(project, scope, ".", null, default));
    }

    [Fact]
    public async Task Function_calls_lists_the_recorded_calls_and_callers_and_queues_what_is_not_recorded()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddNodeAsync(NodeRole.ToolsNode, "tools-model");
        harness.Project.Write("Controllers/KlaimPendingController.cs", "public class KlaimPendingController {\n    public object InitiateData() {\n        return null;\n    }\n}\n");
        harness.Project.Write("Scripts/klaim.js", Script);
        harness.Router.On(ScriptedAgent.FunctionCalls, ScriptedRouter.Text("""
            {"functions":[
              {"id":"F1","calls":[{"kind":"function","target":"p_InitiateDetail","line":2},{"kind":"function","target":"p_initiateData","line":3}]},
              {"id":"F2","calls":[{"kind":"http","target":"POST /Transaction/KlaimPending/InitiateData","line":7}]}]}
            """));
        await harness.Service<IFunctionCallScanner>().ScanFileAsync(harness.ProjectId, harness.Project.Root, "Scripts/klaim.js", [(1, 8)], CancellationToken.None);
        var (project, scope) = await ContextAsync(harness);
        var tools = Tools(harness);

        var init = await tools.CallsAsync(project, scope, "Scripts/klaim.js", "p_InitForm", default);
        Assert.Contains("Scripts/klaim.js:1-4", init);
        Assert.Contains("  3: function p_initiateData → Scripts/klaim.js:6 p_initiateData", init);
        Assert.Contains("Called by: none recorded", init);

        var action = await tools.CallsAsync(project, scope, "Controllers/KlaimPendingController.cs", "InitiateData", default);
        Assert.Contains("Calls: not recorded yet", action);
        Assert.Contains("  Scripts/klaim.js:7 p_initiateData", action);
        Assert.Contains("[calls: 2]", await tools.OutlineAsync(project, scope, "Scripts/klaim.js", null, default));

        // The controller action was queued for the tools node.
        Assert.True(harness.Service<IFunctionCallQueue>().Reader.TryRead(out var queued));
        Assert.Equal("Controllers/KlaimPendingController.cs", queued!.Path);
        Assert.StartsWith("No function or method 'Missing'", await tools.CallsAsync(project, scope, "Scripts/klaim.js", "Missing", default));
    }

    [Fact]
    public void The_planner_always_has_the_skeleton_tools_and_a_tools_node_that_reads_files_too()
    {
        var planner = PlannerTools.All(groups: [new ToolsNodeGroup("scout", new Common.Entities.AiNode { Name = "Scout" }, [PlannerTools.ReadFile])])
            .Select(t => t!["function"]!["name"]!.GetValue<string>()).ToList();
        Assert.Contains(PlannerTools.CodeOutline, planner);
        Assert.Contains(PlannerTools.FunctionCalls, planner);
        Assert.DoesNotContain(PlannerTools.ReadFile, planner);

        Assert.Equal([PlannerTools.ReadFile, PlannerTools.CodeOutline, PlannerTools.FunctionCalls], PlannerTools.ToolsNodeTools([PlannerTools.ReadFile]));
        Assert.Equal([PlannerTools.SearchMemory], PlannerTools.ToolsNodeTools([PlannerTools.SearchMemory]));
    }

    [Fact]
    public async Task The_brief_outlines_the_files_the_request_names()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddStandardNodesAsync();
        harness.Project.Write("Scripts/klaim.js", Script);
        harness.Router.On(ScriptedAgent.Planner, harness.Router.SubmitPlan(new ThreadPlan { Summary = "Jawab.", Answer = "Ada tiga function." }));

        await harness.RunAsync("Apa saja function di Scripts/klaim.js?");

        var brief = harness.Router.CallsTo(ScriptedAgent.Planner)[0].Messages[1]!["content"]!.ToString();
        Assert.Contains("# Outline of the files the request names", brief);
        Assert.Contains("  6-8 function p_initiateData()", brief);
    }

    private static ICodeSkeletonTools Tools(OrchestrationHarness harness) => harness.Service<ICodeSkeletonTools>();

    private static async Task<(ProjectContext Project, WorkspaceScope Scope)> ContextAsync(OrchestrationHarness harness)
    {
        var project = (await harness.Service<IProjectService>().GetContextAsync(harness.ProjectId))!;
        return (project, new WorkspaceScope(project.RootPath));
    }
}
