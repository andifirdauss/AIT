using System.Text;
using System.Text.Json.Nodes;
using AiThread.Common.Entities;
using AiThread.Common.Enums;
using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

// The tools a planner delegated to one tools node. Key is how the planner names it in investigate.
public sealed record ToolsNodeGroup(string Key, AiNode Node, IReadOnlyList<string> Tools);

public sealed record ToolsNodeInvestigation(string Findings, int Steps, int Tokens, int CachedTokens, AiNode Node);

// A cheap model that answers one planner question with the planner's delegated tools, in its own tool-calling loop,
// and returns only its findings: the raw tool output stays in this conversation, which ends with the call.
public sealed class ToolsNodeAgent(NodeCaller caller, PlannerToolRunner runner, NodePromptBuilder prompts)
{
    private const int MaxSteps = 12;
    private const int MaxFindingsChars = 12_000;

    // label numbers the investigation in the progress ("#1"), since several can run at once.
    public async Task<ToolsNodeInvestigation> InvestigateAsync(
        ToolsNodeGroup group,
        string question,
        PlannerInput input,
        WorkspaceTools tools,
        HashSet<Guid> memoriesRead,
        string label,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var node = group.Node;
        // › is in every monospace font (↳ is not, and falls back to an odd glyph in Consolas/Cascadia).
        var prefix = $"{label} › ";
        var actor = $"Tools node '{node.Name}' {label}";
        var toolList = PlannerTools.ForToolsNode(group.Tools);
        var messages = new JsonArray { await SystemMessageAsync(node, cancellationToken), ChatMessages.User(Brief(input, question)) };
        var tokens = 0;
        var cached = 0;

        for (var step = 1; ; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var forced = step > MaxSteps;
            if (step == MaxSteps)
                messages.Add(ChatMessages.User("You have reached the step limit. Call report_findings now with what you found so far."));

            var current = node;
            var reply = await caller.ChatAsync(current, async candidate =>
            {
                if (candidate.Id != current.Id) messages[0] = await SystemMessageAsync(candidate, cancellationToken);
                return new ChatCompletionRequest
                {
                    Model = candidate.Model,
                    Messages = messages,
                    Tools = forced ? PlannerTools.ReportOnly() : toolList,
                    Temperature = candidate.Temperature ?? 0.1,
                    MaxTokens = candidate.MaxTokens
                };
            }, cancellationToken, onSwitch: s => s.ReportAsync($"Tools node {label}", recorder, progress, cancellationToken));
            node = reply.Node;
            var result = reply.Result;
            tokens += result.TotalTokens;
            cached += result.CachedTokens;

            // A plain reply is the answer too; so is anything after the forced step.
            if (result.ToolCalls.Count == 0 || forced)
                return Done(result.ToolCalls.Count == 0 ? result.Content : Findings(result.ToolCalls) ?? result.Content);

            if (Findings(result.ToolCalls) is { } findings) return Done(findings);

            messages.Add(ChatMessages.Assistant(string.IsNullOrWhiteSpace(result.Content) ? null : result.Content, result.ToolCalls));
            var outcomes = await Task.WhenAll(result.ToolCalls.Select(call => RunToolAsync(call, group, input, tools, memoriesRead, prefix, actor, recorder, progress, cancellationToken)));
            for (var i = 0; i < result.ToolCalls.Count; i++) messages.Add(ChatMessages.Tool(result.ToolCalls[i].Id, outcomes[i].Output));

            ToolsNodeInvestigation Done(string text)
            {
                var answer = string.IsNullOrWhiteSpace(text) ? "The sub agent found nothing to report." : PlannerToolRunner.Limit(text.Trim(), MaxFindingsChars);
                recorder.Log(ActivityLevel.Debug, $"{actor} selesai: {step} langkah, {tokens} token, {answer.Length} karakter temuan.");
                return new ToolsNodeInvestigation(answer, step, tokens, cached, node);
            }
        }
    }

    private async Task<PlannerToolOutcome> RunToolAsync(
        ToolCall call,
        ToolsNodeGroup group,
        PlannerInput input,
        WorkspaceTools tools,
        HashSet<Guid> memoriesRead,
        string prefix,
        string actor,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var available = PlannerTools.ToolsNodeTools(group.Tools);
        if (!available.Contains(call.Name))
            return new PlannerToolOutcome($"Error: tool '{call.Name}' is not available. Available: {string.Join(", ", available)}, {PlannerTools.ReportFindings}.", null, true);

        var args = PlannerToolRunner.ParseArguments(call.Arguments, out var error);
        if (args is null) return error!;

        await PlannerToolRunner.AnnounceAsync(call.Name, args, actor, prefix, recorder, progress, cancellationToken);
        return await runner.RunAsync(call.Name, args, input, tools, memoriesRead, prefix, recorder, progress, cancellationToken);
    }

    // The findings of a report_findings call, when the model made one.
    private static string? Findings(IReadOnlyList<ToolCall> calls)
    {
        var report = calls.FirstOrDefault(c => c.Name == PlannerTools.ReportFindings);
        if (report is null) return null;
        var args = PlannerToolRunner.ParseArguments(report.Arguments, out _);
        return args is null ? report.Arguments : PlannerToolRunner.Str(args, "findings") ?? string.Empty;
    }

    // Small on purpose: the tools node finds the code itself, the planner's brief is not repeated.
    private static string Brief(PlannerInput input, string question)
    {
        var project = input.Project;
        var builder = new StringBuilder();
        builder.Append("# Project\n");
        builder.Append($"Name: {project.Name}\n");
        builder.Append($"Root: {project.RootPath}\n");
        if (project.Stacks.Count > 0) builder.Append($"Stack: {string.Join(", ", project.Stacks)}\n");
        builder.Append("Tool paths are relative to the root. The read tools also accept absolute paths outside the root (read-only).\n\n");
        builder.Append("# Question from the planner\n");
        builder.Append(question.Trim()).Append('\n');
        return builder.ToString();
    }

    private async Task<JsonObject> SystemMessageAsync(AiNode node, CancellationToken cancellationToken) =>
        ChatMessages.System(await prompts.BuildAsync(AgentPrompts.ToolsNode, node, cancellationToken, canReadFiles: true));
}
