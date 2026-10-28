using System.Diagnostics;
using System.Text.Json.Nodes;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class ToolsNodeAgent
{
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
        // A tools node never pauses the thread for the developer's permission: run_command runs only what needs none.
        input = input with { CanApprove = false };
        // › is in every monospace font (↳ is not, and falls back to an odd glyph in Consolas/Cascadia).
        var prefix = $"{label} › ";
        var actor = $"Tools node '{node.Name}' {label}";
        var toolList = PlannerTools.ForToolsNode(group.Tools);
        var messages = new JsonArray { await SystemMessageAsync(node, cancellationToken), ChatMessages.User(Brief(input, tools, question)) };
        var tokens = 0;
        var cached = 0;
        var clock = Stopwatch.StartNew();
        var modelTime = TimeSpan.Zero;
        var queueTime = TimeSpan.Zero;

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
                    MaxTokens = candidate.MaxTokens,
                    // Shared by the thread's investigations: they start with the same system prompt, tools and project section.
                    PromptCacheKey = ChatCompletionRequest.CacheKey(recorder.Thread.Id, "tools")
                };
            }, cancellationToken, onSwitch: s => s.ReportAsync($"Tools node {label}", recorder, progress, cancellationToken));
            node = reply.Node;
            var result = reply.Result;
            tokens += result.TotalTokens;
            cached += result.CachedTokens;
            modelTime += reply.Elapsed;
            queueTime += reply.Queued;

            // A plain reply is the answer too; so is anything after the forced step.
            var report = result.ToolCalls.Count == 0 ? null : Findings(result.ToolCalls);
            if (result.ToolCalls.Count == 0 || forced || report is not null)
            {
                var excerpts = report is { } named
                    ? await ExcerptsAsync(named.Excerpts, input, tools, memoriesRead, prefix, actor, recorder, progress, cancellationToken)
                    : string.Empty;
                return Done(report?.Text ?? result.Content, excerpts);
            }

            messages.Add(ChatMessages.Assistant(string.IsNullOrWhiteSpace(result.Content) ? null : result.Content, result.ToolCalls));
            // Token counts exist per model turn, not per tool: the turn's badge goes on its first tool line.
            var badgeHolder = result.ToolCalls.FirstOrDefault(c => c.Name != PlannerTools.SubmitPlan && c.Name != PlannerTools.RunBuild);
            var badge = ClientProgress.TokenBadge(result.PromptTokens, result.CompletionTokens, result.CachedTokens, reply.Elapsed);
            var outcomes = await Task.WhenAll(result.ToolCalls.Select(call => RunToolAsync(call, group, input, tools, memoriesRead, prefix, actor, recorder, progress, ReferenceEquals(call, badgeHolder) ? badge : null, cancellationToken)));
            for (var i = 0; i < result.ToolCalls.Count; i++) messages.Add(ChatMessages.Tool(result.ToolCalls[i].Id, outcomes[i].Output));

            ToolsNodeInvestigation Done(string text, string excerpts)
            {
                var answer = string.IsNullOrWhiteSpace(text) && excerpts.Length == 0
                    ? "The sub agent found nothing to report."
                    : PlannerToolRunner.Limit(text.Trim(), MaxFindingsChars);
                recorder.Log(ActivityLevel.Debug, Lang.T($"{actor} selesai: {step} langkah, {tokens} token, {answer.Length} karakter temuan, ", $"{actor} done: {step} steps, {tokens} tokens, {answer.Length} characters of findings, ")
                                                  + Lang.T($"{FinalReport.Duration(clock.Elapsed)} (model {FinalReport.Duration(modelTime)}, laporan {FinalReport.Duration(reply.Elapsed)}, antre {FinalReport.Duration(queueTime)}).", $"{FinalReport.Duration(clock.Elapsed)} (model {FinalReport.Duration(modelTime)}, report {FinalReport.Duration(reply.Elapsed)}, queued {FinalReport.Duration(queueTime)})."));
                return new ToolsNodeInvestigation(answer + excerpts, step, tokens, cached, node, clock.Elapsed, modelTime, queueTime, reply.Elapsed);
            }
        }
    }
}
