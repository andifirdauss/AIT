using System.Text;
using System.Text.Json;
using AiThread.Service.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace AiThread.API.Infrastructure;

// An orchestrated answer is one growing piece of text; each client protocol dresses it in its own stream format.
public interface IClientStreamWriter : IAsyncDisposable
{
    Task StartAsync(int inputTokens, CancellationToken cancellationToken);

    Task TextAsync(string text, CancellationToken cancellationToken);

    Task FinishAsync(CancellationToken cancellationToken);
}

// Shared SSE plumbing: response headers, serialized writes, and a keep-alive while the planner and executors are busy.
public abstract class SseStreamWriter(HttpResponse response) : IClientStreamWriter
{
    private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly CancellationTokenSource _keepAlive = new();
    private Task? _keepAliveLoop;
    private long _outputChars;
    private bool _finished;

    protected int InputTokens { get; private set; }

    protected long OutputTokens => Math.Max(1, Interlocked.Read(ref _outputChars) / 4);

    public async Task StartAsync(int inputTokens, CancellationToken cancellationToken)
    {
        InputTokens = inputTokens;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers.CacheControl = "no-cache";
        response.Headers["X-Accel-Buffering"] = "no";
        response.HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        await OnStartAsync(cancellationToken);
        _keepAliveLoop = KeepAliveAsync(_keepAlive.Token);
    }

    public Task TextAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text)) return Task.CompletedTask;
        Interlocked.Add(ref _outputChars, text.Length);
        return OnTextAsync(text, cancellationToken);
    }

    public async Task FinishAsync(CancellationToken cancellationToken)
    {
        if (_finished) return;
        _finished = true;
        await StopKeepAliveAsync();
        await OnFinishAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await StopKeepAliveAsync();
        _keepAlive.Dispose();
    }

    protected abstract Task OnStartAsync(CancellationToken cancellationToken);

    protected abstract Task OnTextAsync(string text, CancellationToken cancellationToken);

    protected abstract Task OnFinishAsync(CancellationToken cancellationToken);

    // An SSE comment is ignored by every client parser, which is all a keep-alive needs.
    protected virtual Task WriteKeepAliveAsync(CancellationToken cancellationToken) => WriteRawAsync(": ping\n\n", cancellationToken);

    protected Task WriteFrameAsync(string? eventName, object payload, CancellationToken cancellationToken) =>
        WriteRawAsync((eventName is null ? "" : $"event: {eventName}\n") + $"data: {JsonSerializer.Serialize(payload)}\n\n", cancellationToken);

    protected async Task WriteRawAsync(string frame, CancellationToken cancellationToken)
    {
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

    private async Task KeepAliveAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(KeepAliveInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await WriteKeepAliveAsync(cancellationToken);
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
}

// OpenAI Chat Completions stream: chat.completion.chunk frames, then [DONE].
public sealed class OpenAiChatSseWriter(HttpResponse response, string model) : SseStreamWriter(response)
{
    private readonly string _id = "chatcmpl-aithread-" + Guid.NewGuid().ToString("N")[..20];
    private readonly long _created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    protected override Task OnStartAsync(CancellationToken cancellationToken) =>
        WriteFrameAsync(null, Chunk(new { role = "assistant", content = "" }, null), cancellationToken);

    protected override Task OnTextAsync(string text, CancellationToken cancellationToken) =>
        WriteFrameAsync(null, Chunk(new { content = text }, null), cancellationToken);

    protected override async Task OnFinishAsync(CancellationToken cancellationToken)
    {
        await WriteFrameAsync(null, new
        {
            id = _id,
            @object = "chat.completion.chunk",
            created = _created,
            model,
            choices = new[] { new { index = 0, delta = new { }, finish_reason = "stop" } },
            usage = new { prompt_tokens = InputTokens, completion_tokens = OutputTokens, total_tokens = InputTokens + OutputTokens }
        }, cancellationToken);
        await WriteRawAsync("data: [DONE]\n\n", cancellationToken);
    }

    private object Chunk(object delta, string? finishReason) => new
    {
        id = _id,
        @object = "chat.completion.chunk",
        created = _created,
        model,
        choices = new[] { new { index = 0, delta, finish_reason = finishReason } }
    };
}

// OpenAI Responses stream (Codex): response.created -> output_item.added -> content_part.added -> output_text.delta*
// -> output_text.done -> content_part.done -> output_item.done -> response.completed.
public sealed class OpenAiResponsesSseWriter(HttpResponse response, string model) : SseStreamWriter(response)
{
    private readonly string _responseId = "resp_aithread_" + Guid.NewGuid().ToString("N")[..24];
    private readonly string _itemId = "msg_aithread_" + Guid.NewGuid().ToString("N")[..24];
    private readonly long _created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private readonly StringBuilder _text = new();
    private int _sequence;

    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        await EventAsync("response.created", new { response = Response("in_progress", [], null) }, cancellationToken);
        await EventAsync("response.in_progress", new { response = Response("in_progress", [], null) }, cancellationToken);
        await EventAsync("response.output_item.added", new
        {
            output_index = 0,
            item = new { id = _itemId, type = "message", status = "in_progress", role = "assistant", content = Array.Empty<object>() }
        }, cancellationToken);
        await EventAsync("response.content_part.added", new
        {
            item_id = _itemId,
            output_index = 0,
            content_index = 0,
            part = new { type = "output_text", text = "", annotations = Array.Empty<object>() }
        }, cancellationToken);
    }

    protected override Task OnTextAsync(string text, CancellationToken cancellationToken)
    {
        lock (_text) _text.Append(text);
        return EventAsync("response.output_text.delta", new { item_id = _itemId, output_index = 0, content_index = 0, delta = text }, cancellationToken);
    }

    protected override async Task OnFinishAsync(CancellationToken cancellationToken)
    {
        string full;
        lock (_text) full = _text.ToString();

        var part = new { type = "output_text", text = full, annotations = Array.Empty<object>() };
        var item = new { id = _itemId, type = "message", status = "completed", role = "assistant", content = new[] { part } };

        await EventAsync("response.output_text.done", new { item_id = _itemId, output_index = 0, content_index = 0, text = full }, cancellationToken);
        await EventAsync("response.content_part.done", new { item_id = _itemId, output_index = 0, content_index = 0, part }, cancellationToken);
        await EventAsync("response.output_item.done", new { output_index = 0, item }, cancellationToken);
        await EventAsync("response.completed", new
        {
            response = Response("completed", [item], new { input_tokens = InputTokens, output_tokens = OutputTokens, total_tokens = InputTokens + OutputTokens })
        }, cancellationToken);
    }

    private object Response(string status, object[] output, object? usage) => new
    {
        id = _responseId,
        @object = "response",
        created_at = _created,
        status,
        model,
        output,
        usage
    };

    // Every event carries its own type and a running sequence number, as the Responses API does.
    private Task EventAsync(string type, object fields, CancellationToken cancellationToken)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(fields))!.AsObject();
        node["type"] = type;
        node["sequence_number"] = Interlocked.Increment(ref _sequence) - 1;
        return WriteFrameAsync(type, node, cancellationToken);
    }
}

// Gemini streamGenerateContent (alt=sse): each frame is a GenerateContentResponse; the last one has finishReason.
public sealed class GeminiSseWriter(HttpResponse response, string model) : SseStreamWriter(response)
{
    protected override Task OnStartAsync(CancellationToken cancellationToken) =>
        WriteFrameAsync(null, Candidate("", null, null), cancellationToken);

    protected override Task OnTextAsync(string text, CancellationToken cancellationToken) =>
        WriteFrameAsync(null, Candidate(text, null, null), cancellationToken);

    protected override Task OnFinishAsync(CancellationToken cancellationToken) =>
        WriteFrameAsync(null, Candidate("", "STOP", new
        {
            promptTokenCount = InputTokens,
            candidatesTokenCount = OutputTokens,
            totalTokenCount = InputTokens + OutputTokens
        }), cancellationToken);

    private object Candidate(string text, string? finishReason, object? usage)
    {
        var content = new { role = "model", parts = new[] { new { text } } };
        return finishReason is null
            ? new { candidates = new[] { new { content, index = 0 } }, modelVersion = model }
            : new { candidates = new[] { new { content, finishReason, index = 0 } }, usageMetadata = usage, modelVersion = model };
    }
}

// Non-stream bodies, error bodies and writer selection per client protocol.
public static class ClientResponses
{
    public static IClientStreamWriter CreateWriter(ClientProtocol protocol, HttpResponse response, string model) => protocol switch
    {
        ClientProtocol.OpenAiChat => new OpenAiChatSseWriter(response, model),
        ClientProtocol.OpenAiResponses => new OpenAiResponsesSseWriter(response, model),
        ClientProtocol.GeminiGenerate => new GeminiSseWriter(response, model),
        _ => new AnthropicSseWriter(response, model)
    };

    public static object Message(ClientProtocol protocol, string model, string text, int inputTokens)
    {
        var outputTokens = Math.Max(1, text.Length / 4);
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var suffix = Guid.NewGuid().ToString("N")[..24];

        return protocol switch
        {
            ClientProtocol.OpenAiChat => new
            {
                id = "chatcmpl-aithread-" + suffix,
                @object = "chat.completion",
                created,
                model,
                choices = new[] { new { index = 0, message = new { role = "assistant", content = text }, finish_reason = "stop" } },
                usage = new { prompt_tokens = inputTokens, completion_tokens = outputTokens, total_tokens = inputTokens + outputTokens }
            },
            ClientProtocol.OpenAiResponses => new
            {
                id = "resp_aithread_" + suffix,
                @object = "response",
                created_at = created,
                status = "completed",
                model,
                output = new[]
                {
                    new
                    {
                        id = "msg_aithread_" + suffix,
                        type = "message",
                        status = "completed",
                        role = "assistant",
                        content = new[] { new { type = "output_text", text, annotations = Array.Empty<object>() } }
                    }
                },
                usage = new { input_tokens = inputTokens, output_tokens = outputTokens, total_tokens = inputTokens + outputTokens }
            },
            ClientProtocol.GeminiGenerate => new
            {
                candidates = new[]
                {
                    new { content = new { role = "model", parts = new[] { new { text } } }, finishReason = "STOP", index = 0 }
                },
                usageMetadata = new { promptTokenCount = inputTokens, candidatesTokenCount = outputTokens, totalTokenCount = inputTokens + outputTokens },
                modelVersion = model
            },
            _ => new
            {
                id = "msg_aithread_" + suffix,
                type = "message",
                role = "assistant",
                model,
                content = new[] { new { type = "text", text } },
                stop_reason = "end_turn",
                stop_sequence = (string?)null,
                usage = OrchestratedUsage.Start(inputTokens, outputTokens)
            }
        };
    }

    public static object Error(ClientProtocol protocol, int statusCode, string type, string message) => protocol switch
    {
        ClientProtocol.OpenAiChat or ClientProtocol.OpenAiResponses => new { error = new { message, type, param = (string?)null, code = (string?)null } },
        ClientProtocol.GeminiGenerate => new { error = new { code = statusCode, message, status = GeminiStatus(statusCode) } },
        _ => new { type = "error", error = new { type, message } }
    };

    private static string GeminiStatus(int statusCode) => statusCode switch
    {
        StatusCodes.Status400BadRequest => "INVALID_ARGUMENT",
        StatusCodes.Status401Unauthorized => "UNAUTHENTICATED",
        StatusCodes.Status403Forbidden => "PERMISSION_DENIED",
        StatusCodes.Status404NotFound => "NOT_FOUND",
        StatusCodes.Status429TooManyRequests => "RESOURCE_EXHAUSTED",
        StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable => "UNAVAILABLE",
        _ => "INTERNAL"
    };
}
