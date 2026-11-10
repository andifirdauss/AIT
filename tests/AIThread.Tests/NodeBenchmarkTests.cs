using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;

namespace AIThread.Tests;

public class NodeBenchmarkTests
{
    private static AiNode Node(NodeRole role, string model) => new() { Name = role.ToString(), Role = role, Model = model };

    private static ModelMeasurement Measured(string model, params long[] times) => new(model, times, 0, null);

    [Fact]
    public void Steps_follow_one_small_change()
    {
        var (planner, tools, executor, reviewer, wrapper) = (Node(NodeRole.Planner, "p"), Node(NodeRole.ToolsNode, "t"), Node(NodeRole.Executor, "e"), Node(NodeRole.Reviewer, "r"), Node(NodeRole.Wrapper, "w"));

        var skipped = NodeBenchmarkReport.Steps(planner, tools, executor, reviewer, wrapper, wrapperSkipMaxTasks: 2);
        Assert.Equal([(NodeRole.Planner, 2), (NodeRole.ToolsNode, 3), (NodeRole.Executor, 1), (NodeRole.Reviewer, 1)], skipped.Select(s => (s.Node.Role, s.Calls)));

        var always = NodeBenchmarkReport.Steps(planner, null, executor, null, wrapper, wrapperSkipMaxTasks: 0);
        Assert.Equal([NodeRole.Planner, NodeRole.Executor, NodeRole.Wrapper], always.Select(s => s.Node.Role));
    }

    [Fact]
    public void The_largest_estimate_is_the_bottleneck_and_the_tip_names_a_faster_model()
    {
        using var _ = Lang.Use("en");
        var steps = NodeBenchmarkReport.Steps(Node(NodeRole.Planner, "fast"), Node(NodeRole.ToolsNode, "slow"), Node(NodeRole.Executor, "fast"), null, null, 2);

        var report = NodeBenchmarkReport.Build(steps, [Measured("fast", 1_000, 1_000, 1_000), Measured("slow", 8_000, 8_000, 8_000)], 3);

        // 2 x 1 s + 3 x 8 s + 1 x 1 s.
        Assert.Equal(27_000, report.EstimatedMs);
        Assert.Equal("ToolsNode", report.BottleneckNode);
        var slow = Assert.Single(report.Stages, s => s.IsBottleneck);
        Assert.Equal(24_000, slow.EstimatedMs);
        Assert.Equal(89, slow.SharePercent);
        Assert.Contains(report.Tips, t => t.Contains("slow spot") && t.Contains("fast is much faster"));
        Assert.Contains(report.Tips, t => t.Contains("Planner reads directly"));
    }

    [Fact]
    public void A_slow_planner_is_not_told_to_use_a_faster_model_as_if_it_cost_nothing()
    {
        using var _ = Lang.Use("en");
        var steps = NodeBenchmarkReport.Steps(Node(NodeRole.Planner, "strong"), null, Node(NodeRole.Executor, "fast"), null, null, 2);

        var report = NodeBenchmarkReport.Build(steps, [Measured("strong", 6_000, 6_000, 6_000), Measured("fast", 1_000, 1_000, 1_000)], 3);

        Assert.Contains(report.Tips, t => t.Contains("normal for the strongest model") && t.Contains("risk"));
        Assert.DoesNotContain(report.Tips, t => t.Contains("much faster"));
    }

    [Fact]
    public void A_swinging_model_is_marked_unstable_and_a_steady_one_is_not()
    {
        using var _ = Lang.Use("en");
        var steps = NodeBenchmarkReport.Steps(Node(NodeRole.Planner, "steady"), null, Node(NodeRole.Executor, "swing"), null, null, 2);

        var report = NodeBenchmarkReport.Build(steps, [Measured("steady", 2_000, 2_200, 2_100), Measured("swing", 6_000, 10_000, 21_000)], 3);

        Assert.False(report.Stages[0].Unstable);
        Assert.True(report.Stages[1].Unstable);
        Assert.Contains(report.Tips, t => t.Contains("swing") && t.Contains("swings"));
    }

    [Fact]
    public void Fast_steady_nodes_get_a_plain_all_clear()
    {
        using var _ = Lang.Use("en");
        var steps = NodeBenchmarkReport.Steps(Node(NodeRole.Planner, "a"), null, Node(NodeRole.Executor, "b"), null, null, 2);

        var report = NodeBenchmarkReport.Build(steps, [Measured("a", 1_000, 1_000, 1_000), Measured("b", 900, 900, 900)], 3);

        Assert.Contains("No slow spot", Assert.Single(report.Tips));
    }

    [Fact]
    public void No_active_node_says_what_to_add()
    {
        using var _ = Lang.Use("en");

        var report = NodeBenchmarkReport.Build([], [], 3);

        Assert.Empty(report.Stages);
        Assert.Null(report.BottleneckNode);
        Assert.Contains("no active node", Assert.Single(report.Tips));
    }

    [Fact]
    public async Task A_model_shared_by_several_nodes_is_measured_once()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddNodeAsync(NodeRole.Planner, "strong");
        await harness.AddNodeAsync(NodeRole.Executor, "cheap");
        await harness.AddNodeAsync(NodeRole.Reviewer, "cheap");
        await harness.AddNodeAsync(NodeRole.Wrapper, "cheap");
        foreach (var _ in Enumerable.Range(0, 6)) harness.Router.On(ScriptedAgent.Other, ScriptedRouter.Text("It totals order lines."));

        var report = await harness.Benchmark.RunAsync();

        Assert.Equal([NodeRole.Planner, NodeRole.Executor, NodeRole.Reviewer, NodeRole.Wrapper], report.Stages.Select(s => s.Role));
        Assert.Equal(3, harness.Router.CallsTo(ScriptedAgent.Other).Count(c => c.Model == "strong"));
        Assert.Equal(3, harness.Router.CallsTo(ScriptedAgent.Other).Count(c => c.Model == "cheap"));
    }

    [Fact]
    public async Task A_planner_that_reads_directly_does_not_count_its_tools_node()
    {
        await using var harness = await OrchestrationHarness.CreateAsync();
        var planner = await harness.AddNodeAsync(NodeRole.Planner, "strong", directRead: true);
        await harness.AddNodeAsync(NodeRole.Executor, "cheap");
        var scout = await harness.AddNodeAsync(NodeRole.ToolsNode, "scout");
        await ToolsNodeTests.DelegateAsync(harness, planner.Id, scout.Id, PlannerTools.ReadFile);
        foreach (var _ in Enumerable.Range(0, 6)) harness.Router.On(ScriptedAgent.Other, ScriptedRouter.Text("It totals order lines."));

        var report = await harness.Benchmark.RunAsync();

        Assert.DoesNotContain(report.Stages, s => s.Role == NodeRole.ToolsNode);
        Assert.DoesNotContain(harness.Router.Calls, c => c.Model == "scout");
    }

    [Fact]
    public async Task A_failing_model_is_reported_without_failing_the_benchmark()
    {
        using var _ = Lang.Use("en");
        await using var harness = await OrchestrationHarness.CreateAsync();
        await harness.AddNodeAsync(NodeRole.Planner, "down");
        foreach (var __ in Enumerable.Range(0, 3)) harness.Router.On(ScriptedAgent.Other, _ => throw new NineRouterException("Too many requests", 429));

        var report = await harness.Benchmark.RunAsync();

        var stage = Assert.Single(report.Stages);
        Assert.Equal(3, stage.Failed);
        Assert.Null(stage.MedianMs);
        Assert.Null(report.BottleneckNode);
        Assert.Contains(report.Tips, t => t.Contains("failed to answer 3 time(s)") && t.Contains("Too many requests"));
    }
}
