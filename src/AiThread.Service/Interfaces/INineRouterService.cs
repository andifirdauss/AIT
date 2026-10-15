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

    // Remaining quota of the 9router accounts serving the given model/combo ids, from 9router's dashboard API
    // (/api/*, open only while its "require login" is off). Best effort: empty when 9router does not answer.
    Task<IReadOnlyList<RouterQuotaDto>> GetQuotasAsync(IReadOnlyCollection<string> models, CancellationToken cancellationToken = default);

    // Forwards a raw request in the client's own format (Anthropic Messages, OpenAI chat/responses, Gemini) to the
    // matching 9router endpoint, which translates it for the target provider. Caller disposes the response.
    Task<HttpResponseMessage> ForwardAsync(
        string pathAndQuery,
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default);
}
