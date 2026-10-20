using System.Text.Json.Nodes;
using AiThread.Service.Models;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class ExecutorAgent
{
    public async Task<ExecutorOutcome> ExecuteAsync(ExecutorJob job, CancellationToken cancellationToken)
    {
        var task = job.Task;
        if (!job.Scope.TryResolve(task.File, out var fullPath, out var pathError))
            return new ExecutorOutcome(false, null, null, pathError, 0, 0);

        var initial = await TextFileIo.ReadAsync(fullPath, cancellationToken);
        var messages = new JsonArray
        {
            ChatMessages.System(await prompts.BuildAsync(AgentPrompts.Executor, job.Node, cancellationToken)),
            ChatMessages.User(await BuildTaskMessageAsync(job, initial, cancellationToken))
        };

        var tokens = 0;
        var maxAttempts = 1 + Math.Max(0, job.MaxRetries);
        string? lastError = null;
        var node = job.Node;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            await job.OnAttempt(attempt);

            ChatCompletionResult result;
            try
            {
                var current = node;
                var reply = await caller.ChatAsync(current, async candidate =>
                {
                    if (candidate.Id != current.Id) messages[0] = ChatMessages.System(await prompts.BuildAsync(AgentPrompts.Executor, candidate, cancellationToken));
                    return new ChatCompletionRequest
                    {
                        Model = candidate.Model,
                        Messages = messages,
                        Temperature = candidate.Temperature ?? 0.1,
                        MaxTokens = candidate.MaxTokens,
                        PromptCacheKey = job.CacheKey
                    };
                }, cancellationToken, job.OnOutput, job.OnSwitch, slotHeld: current.Id == job.Node.Id);
                node = reply.Node;
                result = reply.Result;
            }
            catch (NineRouterException ex)
            {
                return new ExecutorOutcome(false, null, null, ex.Message, tokens, attempt);
            }

            tokens += result.TotalTokens;
            var parsed = EditBlockParser.Parse(result.Content);

            string feedback;
            if (parsed.Blocks.Count == 0)
            {
                lastError = result.FinishReason == "length"
                    ? "Output was cut off (max tokens) before any complete edit block."
                    : "No valid SEARCH/REPLACE block in the output.";
                feedback = $"{lastError} Respond again using exactly the required format: edit blocks followed by one SUMMARY line.";
            }
            else
            {
                var applied = await ApplyAsync(job, fullPath, parsed, cancellationToken);
                if (applied.Outcome is { } done) return done with { Tokens = tokens, Attempts = attempt };

                lastError = string.Join(" ", applied.Errors);
                feedback = $"""
                    Your edit could not be applied, nothing was written:
                    - {string.Join("\n- ", applied.Errors)}

                    {await CurrentContentSectionAsync(job, fullPath, cancellationToken)}

                    Output corrected edit blocks for the whole task (not only the failed block), then the SUMMARY line.
                    """;
            }

            messages.Add(ChatMessages.Assistant(result.Content));
            messages.Add(ChatMessages.User(feedback));
        }

        return new ExecutorOutcome(false, null, null, lastError ?? "Executor gagal.", tokens, maxAttempts);
    }
}
