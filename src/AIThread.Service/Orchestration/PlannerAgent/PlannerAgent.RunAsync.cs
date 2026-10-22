using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    private async Task<PlanningResult> RunAsync(
        PlannerInput input,
        PlannerRun run,
        WorkspaceTools tools,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        var messages = run.Messages;
        var node = run.Node;
        var textReplies = 0;

        // The same tool list on every step: changing it invalidates the provider's prompt cache for the whole
        // conversation. Only a planner that ignored the "submit now" message gets a submit-only extra step.
        var allTools = PlannerTools.All(run.CanBuild, input.CanAsk, run.Groups, input.AutoTesting, run.CanGit, input.UnitTests is not null);

        while (run.Step < run.MaxSteps + 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = ++run.Step;
            var forcedStep = step > run.MaxSteps;
            if (step == run.MaxSteps)
                messages.Add(ChatMessages.User("You have reached the investigation limit. Call submit_plan now with the best plan you can make from what you already know."));

            // A slot is held only while the model answers, not while tools run, so concurrent threads share the planner.
            // The conversation so far is plain OpenAI chat, so a fallback planner continues where this one stopped.
            var current = node;
            var reply = await caller.ChatAsync(current, async candidate =>
            {
                if (candidate.Id != current.Id) messages[0] = await SystemMessageAsync(candidate, run.Groups, input.AutoTesting, input.UnitTests?.Settings.Authoring, cancellationToken);
                return new ChatCompletionRequest
                {
                    Model = candidate.Model,
                    Messages = messages,
                    Tools = forcedStep ? PlannerTools.SubmitOnly(input.AutoTesting, input.UnitTests is not null) : allTools,
                    Temperature = candidate.Temperature ?? 0.2,
                    MaxTokens = candidate.MaxTokens,
                    PromptCacheKey = ChatCompletionRequest.CacheKey(recorder.Thread.Id, "planner")
                };
            }, cancellationToken, onSwitch: s => s.ReportAsync("Planner", recorder, progress, cancellationToken));
            node = run.Node = reply.Node;
            var result = reply.Result;
            run.Tokens += result.TotalTokens;
            run.CachedTokens += result.CachedTokens;

            if (result.ToolCalls.Count == 0)
            {
                messages.Add(ChatMessages.Assistant(result.Content));

                // Some models ignore tool calling and print the plan as JSON.
                if (ThreadPlan.TryParseFromText(result.Content, out var textPlan) && textPlan is not null)
                {
                    var errors = PlanValidator.Validate(textPlan, input.Scope, input.AutoTesting, input.UnitTests?.Rules);
                    if (errors.Count == 0) return Finished(textPlan, run, node);
                    messages.Add(ChatMessages.User($"The plan is invalid:\n- {string.Join("\n- ", errors)}\nFix it and call submit_plan."));
                    continue;
                }

                if (++textReplies > MaxTextNudges)
                    return DirectAnswer(result.Content, run, node, recorder);

                messages.Add(ChatMessages.User("Do not answer in plain text. Use the tools to investigate if needed, then call submit_plan (put the full answer in its \"answer\" field when no file change is needed)."));
                continue;
            }

            messages.Add(ChatMessages.Assistant(string.IsNullOrWhiteSpace(result.Content) ? null : result.Content, result.ToolCalls));

            // Token counts exist per model turn, not per tool: the turn's badge goes on its first tool line.
            var badgeHolder = result.ToolCalls.FirstOrDefault(c => c.Name is not (PlannerTools.SubmitPlan or PlannerTools.RunBuild));
            var badge = ClientProgress.TokenBadge(result.PromptTokens, result.CompletionTokens, result.CachedTokens);
            var outcomes = await Task.WhenAll(result.ToolCalls.Select(call => RunToolAsync(call, input, run, tools, recorder, progress, ReferenceEquals(call, badgeHolder) ? badge : null, cancellationToken)));

            ThreadPlan? accepted = null;
            (ToolCall Call, PlannerQuestion Question)? ask = null;
            for (var i = 0; i < result.ToolCalls.Count; i++)
            {
                var call = result.ToolCalls[i];
                if (outcomes[i].Question is { } question)
                {
                    // The answer becomes this call's result when the thread resumes.
                    if (ask is null)
                    {
                        ask = (call, question);
                        continue;
                    }
                    messages.Add(ChatMessages.Tool(call.Id, "Error: only one question at a time; this one was not shown to the developer."));
                    continue;
                }
                messages.Add(ChatMessages.Tool(call.Id, outcomes[i].Output));
                accepted ??= outcomes[i].AcceptedPlan;
            }

            if (accepted is not null) return Finished(accepted, run, node);
            if (ask is { } pause)
            {
                run.QuestionsAsked++;
                var conversation = new PlannerConversation(messages, node.Id, run.Step, run.Tokens, run.CachedTokens, pause.Call.Id, run.QuestionsAsked, run.CanBuild, run.ToolsNodeTokens, run.CanGit);
                return new PlanningResult(null, run.Step, run.Tokens, run.CachedTokens, node, new PlannerPause(pause.Question, conversation), run.MemoriesRead.Count, run.ToolsNodeTokens);
            }
        }

        throw new OrchestrationException($"Planner tidak menghasilkan rencana yang valid dalam {run.MaxSteps} langkah.");
    }
}
