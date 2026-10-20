using AIThread.Service.Models;
using Microsoft.AspNetCore.Http;

namespace AIThread.API.Infrastructure;

// Emits an Anthropic Messages API stream with a single text block:
// message_start -> content_block_start -> content_block_delta* -> content_block_stop -> message_delta -> message_stop.
// Pings keep Claude Code's connection alive while the planner and executors are busy.
public sealed class AnthropicSseWriter(HttpResponse response, string model) : SseStreamWriter(response)
{
    private readonly string _messageId = "msg_aithread_" + Guid.NewGuid().ToString("N")[..24];

    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        await WriteFrameAsync("message_start", new
        {
            type = "message_start",
            message = new
            {
                id = _messageId,
                type = "message",
                role = "assistant",
                model,
                content = Array.Empty<object>(),
                stop_reason = (string?)null,
                stop_sequence = (string?)null,
                usage = OrchestratedUsage.Start(InputTokens, 1)
            }
        }, cancellationToken);
        await WriteFrameAsync("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } }, cancellationToken);
        await WriteFrameAsync("ping", new { type = "ping" }, cancellationToken);
    }

    protected override Task OnTextAsync(string text, CancellationToken cancellationToken) =>
        WriteFrameAsync("content_block_delta", new
        {
            type = "content_block_delta",
            index = 0,
            delta = new { type = "text_delta", text }
        }, cancellationToken);

    protected override async Task OnFinishAsync(CancellationToken cancellationToken)
    {
        await WriteFrameAsync("content_block_stop", new { type = "content_block_stop", index = 0 }, cancellationToken);
        await WriteFrameAsync("message_delta", new
        {
            type = "message_delta",
            delta = new { stop_reason = "end_turn", stop_sequence = (string?)null },
            usage = new { output_tokens = OutputTokens }
        }, cancellationToken);
        await WriteFrameAsync("message_stop", new { type = "message_stop" }, cancellationToken);
    }

    protected override Task WriteKeepAliveAsync(CancellationToken cancellationToken) =>
        WriteFrameAsync("ping", new { type = "ping" }, cancellationToken);
}

// Usage reported for an orchestrated answer. The conversation never reaches Claude (the real planner cost sits in the
// planner CLI's own transcript), but Claude Code sizes its context window from input + cache fields, so the estimate
// is kept, reported as cache reads. Usage trackers that price Claude Code transcripts would otherwise bill every
// orchestrated turn as fresh Opus input.
public static class OrchestratedUsage
{
    public static object Start(int estimatedContextTokens, long outputTokens) => new
    {
        input_tokens = 0,
        cache_creation_input_tokens = 0,
        cache_read_input_tokens = estimatedContextTokens,
        output_tokens = outputTokens
    };
}

// Everything the orchestrator shows the client becomes text deltas of the one text block.
public sealed class SseThreadObserver(IClientStreamWriter writer) : IThreadObserver
{
    public Task OnProgressAsync(string line, CancellationToken cancellationToken) => writer.TextAsync(line, cancellationToken);

    public Task OnFinalDeltaAsync(string delta, CancellationToken cancellationToken) => writer.TextAsync(delta, cancellationToken);
}
