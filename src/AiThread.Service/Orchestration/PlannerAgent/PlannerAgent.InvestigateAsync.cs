using System.Text.Json.Nodes;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // The tools node's tool calls appear under "#n" lines, since investigations run in parallel.
    private async Task<PlannerToolOutcome> InvestigateAsync(
        JsonObject args,
        PlannerInput input,
        PlannerRun run,
        WorkspaceTools tools,
        ThreadRecorder recorder,
        ClientProgress progress,
        string? badge,
        CancellationToken cancellationToken)
    {
        if (run.Groups.Count == 0) return new PlannerToolOutcome("Error: there are no sub agents.", null, true);
        var question = PlannerToolRunner.Str(args, "question");
        if (question is null) return new PlannerToolOutcome("Error: \"question\" is required.", null, true);
        var agent = PlannerToolRunner.Str(args, "agent");
        var group = run.Groups.Count == 1 ? run.Groups[0] : run.Groups.FirstOrDefault(g => string.Equals(g.Key, agent, StringComparison.OrdinalIgnoreCase));
        if (group is null)
            return new PlannerToolOutcome($"Error: \"agent\" must be one of: {string.Join(", ", run.Groups.Select(g => g.Key))}.", null, true);

        var label = $"#{Interlocked.Increment(ref run.Investigations)}";
        var nodePrefix = $"{label} › ";
        var detail = $"{label} {(run.Groups.Count > 1 ? $"[{group.Key}] " : string.Empty)}{PlannerToolRunner.Describe(PlannerTools.Investigate, args)}";
        recorder.PlannerActivity(PlannerTools.Investigate, detail);
        recorder.Log(ActivityLevel.Debug, $"Planner → investigate {label} ({group.Node.Name}): {question}");
        await progress.StepAsync(PlannerToolRunner.Lead(string.Empty, PlannerTools.Investigate) + ClientProgress.BadgeSlot(badge) + detail, cancellationToken, hang: PlannerToolRunner.LeadColumn + ClientProgress.SlotWidth);

        try
        {
            var result = await toolsNodeAgent.InvestigateAsync(group, question, input, tools, run.MemoriesRead, label, recorder, progress, cancellationToken);
            Interlocked.Add(ref run.ToolsNodeTokens, result.Tokens);
            await progress.StepAsync(nodePrefix + ClientProgress.Pad("complete", PlannerTools.NameColumn) + $"{result.Steps} langkah · {FinalReport.Tokens(result.Tokens)} token{(result.CachedTokens > 0 ? $" · c{FinalReport.Tokens(result.CachedTokens)}" : string.Empty)} · {result.Node.Model}", cancellationToken, hang: PlannerToolRunner.Hang(nodePrefix));
            return new PlannerToolOutcome(result.Findings, null);
        }
        catch (NineRouterException ex) when (!cancellationToken.IsCancellationRequested)
        {
            recorder.Log(ActivityLevel.Warning, $"Tools node '{group.Node.Name}' gagal menjawab investigasi {label}: {ex.Message}");
            await progress.StepAsync(nodePrefix + ClientProgress.Pad("failed", PlannerTools.NameColumn) + $"tools node {group.Node.Model} gagal: {ex.Message}", cancellationToken, hang: PlannerToolRunner.Hang(nodePrefix));
            return new PlannerToolOutcome($"Error: the sub agent is unavailable ({ex.Message}). Try again later or plan with what you already know.", null, true);
        }
    }
}
