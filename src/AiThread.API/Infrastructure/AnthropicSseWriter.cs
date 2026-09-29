using System.Text.Json;
using AiThread.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AiThread.API.Infrastructure;

// Emits an Anthropic Messages API stream with a single text block:
// message_start -> content_block_start -> content_block_delta* -> content_block_stop -> message_delta -> message_stop.
// Pings keep Claude Code's connection alive while the planner and executors are busy.
public sealed class AnthropicSseWriter(HttpResponse response, string model) : IAsyncDisposable
{
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _messageId = "msg_aithread_" + Guid.NewGuid().ToString("N")[..24];
    private readonly CancellationTokenSource _keepAlive = new();
    private Task? _keepAliveLoop;
    private long _outputChars;
    private bool _finished;

    public async Task StartAsync(int inputTokens, CancellationToken cancellationToken)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await WriteEventAsync("message_start", new
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
                usage = OrchestratedUsage.Start(inputTokens, 1)
            }
        }, cancellationToken);
        await WriteEventAsync("content_block_start", new { type = "content_block_start", index = 0, content_block = new { type = "text", text = "" } }, cancellationToken);
        await WriteEventAsync("ping", new { type = "ping" }, cancellationToken);

        _keepAliveLoop = KeepAliveAsync(_keepAlive.Token);
    }

    public Task TextAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text)) return Task.CompletedTask;
        Interlocked.Add(ref _outputChars, text.Length);
        return WriteEventAsync("content_block_delta", new
        {
            type = "content_block_delta",
            index = 0,
            delta = new { type = "text_delta", text }
        }, cancellationToken);
    }

    public async Task FinishAsync(CancellationToken cancellationToken)
    {
        if (_finished) return;
        _finished = true;
        await StopKeepAliveAsync();

        await WriteEventAsync("content_block_stop", new { type = "content_block_stop", index = 0 }, cancellationToken);
        await WriteEventAsync("message_delta", new
        {
            type = "message_delta",
            delta = new { stop_reason = "end_turn", stop_sequence = (string?)null },
            usage = new { output_tokens = Math.Max(1, Interlocked.Read(ref _outputChars) / 4) }
        }, cancellationToken);
        await WriteEventAsync("message_stop", new { type = "message_stop" }, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopKeepAliveAsync();
        _keepAlive.Dispose();
    }

    private async Task KeepAliveAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(KeepAliveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await WriteEventAsync("ping", new { type = "ping" }, cancellationToken);
        }
        catch (Exception)
        {
            // Stopped, or the client went away.
        }
    }

    private async Task StopKeepAliveAsync()
    {
        if (!_keepAlive.IsCancellationRequested) await _keepAlive.CancelAsync();
        if (_keepAliveLoop is not null) await _keepAliveLoop;
    }

    private async Task WriteEventAsync(string name, object payload, CancellationToken cancellationToken)
    {
        var frame = $"event: {name}\ndata: {JsonSerializer.Serialize(payload)}\n\n";
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await response.WriteAsync(frame, cancellationToken);
            await response.Body.FlushAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
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
public sealed class SseThreadObserver(AnthropicSseWriter writer) : IThreadObserver
{
    public Task OnProgressAsync(string line, CancellationToken cancellationToken) => writer.TextAsync(line, cancellationToken);

    public Task OnFinalDeltaAsync(string delta, CancellationToken cancellationToken) => writer.TextAsync(delta, cancellationToken);
}
