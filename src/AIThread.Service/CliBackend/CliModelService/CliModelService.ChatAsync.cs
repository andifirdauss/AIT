using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

public sealed partial class CliModelService
{
    public async Task<ChatCompletionResult> ChatAsync(CliModelId model, ChatCompletionRequest request, CliCallLimits limits,
        Func<string, Task>? onDelta = null, CancellationToken cancellationToken = default)
    {
        var engine = _engines.FirstOrDefault(e => e.Tool == model.Tool);
        if (engine is null)
        {
            var supported = string.Join(", ", _engines.Select(e => $"cli:{e.Tool}:<model>"));
            throw new NineRouterException(
                Lang.T($"CLI '{model.Tool}' belum didukung sebagai mesin model (model '{model.Id}'). Yang didukung: {supported}.",
                    $"CLI '{model.Tool}' is not supported as a model engine (model '{model.Id}'). Supported: {supported}."), 400);
        }

        var prompt = CliTranscript.Render(request.Messages, request.Tools);
        var hasTools = request.Tools is { Count: > 0 };
        // With tools, the raw reply holds <tool_call> blocks: it is streamed only once it is known to be a plain answer.
        var output = await engine.RunAsync(model.Model, prompt, limits, hasTools ? null : onDelta, cancellationToken);
        if (!hasTools)
            return new ChatCompletionResult(output.Text, [], output.FinishReason, output.PromptTokens, output.CompletionTokens, output.CachedTokens);

        var parsed = CliToolCallParser.Parse(output.Text);
        if (parsed.ToolCalls.Count == 0 && onDelta is not null && parsed.Content.Length > 0) await onDelta(parsed.Content);
        return new ChatCompletionResult(parsed.Content, parsed.ToolCalls, parsed.ToolCalls.Count > 0 ? "tool_calls" : output.FinishReason,
            output.PromptTokens, output.CompletionTokens, output.CachedTokens);
    }
}
