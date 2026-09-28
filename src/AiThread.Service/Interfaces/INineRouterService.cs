using AiThread.Common.DTOs;
using AiThread.Service.Models;

namespace AiThread.Service.Interfaces;

// Everything that talks to 9router: health, model/combo catalog, chat completions, Anthropic passthrough.
public interface INineRouterService
{
    RouterStatusDto? LastStatus { get; }

    Task<RouterStatusDto> CheckStatusAsync(CancellationToken cancellationToken = default);

    Task<RouterStatusDto> TestConnectionAsync(string? baseUrl, string? apiKey, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RouterModelDto>> GetModelsAsync(bool refresh = false, CancellationToken cancellationToken = default);

    // Streams from 9router internally; onDelta receives text chunks as they arrive.
    Task<ChatCompletionResult> ChatAsync(
        ChatCompletionRequest request,
        Func<string, Task>? onDelta = null,
        CancellationToken cancellationToken = default);

    // Forwards a raw Anthropic Messages API request (Claude Code format) to 9router. Caller disposes the response.
    Task<HttpResponseMessage> ForwardAnthropicAsync(
        string pathAndQuery,
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default);
}
