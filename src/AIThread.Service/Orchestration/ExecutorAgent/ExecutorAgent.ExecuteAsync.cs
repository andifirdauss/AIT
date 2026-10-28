using System.Text.Json.Nodes;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class ExecutorAgent
{
    public async Task<ExecutorOutcome> ExecuteAsync(ExecutorJob job, CancellationToken cancellationToken)
    {
        var task = job.Task;
        if (!job.Scope.TryResolveForWrite(task.File, out var fullPath, out var pathError))
            return new ExecutorOutcome(false, null, null, pathError, 0, 0);

        var initial = await TextFileIo.ReadAsync(fullPath, cancellationToken);
        var messages = new JsonArray
        {
            ChatMessages.System(await prompts.BuildAsync(AgentPrompts.Executor, job.Node, cancellationToken)),
            ChatMessages.User(await BuildTaskMessageAsync(job, initial, cancellationToken))
        };
        // Every attempt's raw output and AIT's answer to it: the companion files of the incident when the task fails.
        var attempts = new List<ExecutorAttempt>();

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
            // A format error rejects the whole output, also when some blocks parsed: half an edit is worse than none.
            if (parsed.Blocks.Count == 0 || parsed.Errors.Count > 0)
            {
                IReadOnlyList<string> errors = result.FinishReason == "length"
                    ? ["The output was cut off (max tokens) before the edit blocks were complete. Use fewer, smaller blocks.", .. parsed.Errors]
                    : parsed.Errors;
                lastError = string.Join(" ", errors);
                feedback = $"""
                    Your reply could not be read as an edit, nothing was written:
                    - {string.Join("\n- ", errors)}

                    Respond again with only edit blocks in exactly this form, then one SUMMARY line:
                    <<<<<<< SEARCH
                    exact lines copied from the current file
                    =======
                    the new lines
                    >>>>>>> REPLACE
                    SUMMARY: one sentence
                    """;
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

            attempts.Add(new ExecutorAttempt(attempt, node.Model, result.FinishReason, result.Content, feedback));
            messages.Add(ChatMessages.Assistant(result.Content));
            messages.Add(ChatMessages.User(feedback));
        }

        var error = lastError ?? Lang.T("Executor gagal.", "Executor failed.");
        if (RecordFailure(job, messages, attempts, error) is { } incident)
            error += Lang.T($" Detail: menu Log → insiden {incident}.", $" Details: Log menu → incident {incident}.");
        return new ExecutorOutcome(false, null, null, error, tokens, maxAttempts);
    }
}
