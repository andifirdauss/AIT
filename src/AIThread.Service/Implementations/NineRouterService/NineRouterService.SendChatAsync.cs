using System.Net.Http.Headers;
using System.Net;
using System.Text.Json.Nodes;
using System.Text;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    private async Task<ChatCompletionResult> SendChatAsync(
        RuntimeSettings settings,
        ChatCompletionRequest request,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model,
            ["messages"] = request.Messages.DeepClone(),
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true }
        };
        if (request.MaxTokens is { } maxTokens) body["max_tokens"] = maxTokens;
        if (request.Temperature is { } temperature) body["temperature"] = temperature;
        if (request.Tools is { Count: > 0 } tools)
        {
            body["tools"] = tools.DeepClone();
            body["tool_choice"] = "auto";
        }
        var sendsCacheKey = request.PromptCacheKey is { Length: > 0 } && !_cacheKeyRejected.ContainsKey(request.Model);
        if (sendsCacheKey) body["prompt_cache_key"] = request.PromptCacheKey;

        using var message = new HttpRequestMessage(HttpMethod.Post, Combine(settings.NineRouterBaseUrl, "/v1/chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        message.Headers.Accept.ParseAdd("text/event-stream");
        if (!string.IsNullOrEmpty(settings.NineRouterApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.NineRouterApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        stall.CancelAfter(_stallTimeout);

        try
        {
            using var response = await Client().SendAsync(message, HttpCompletionOption.ResponseHeadersRead, stall.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(stall.Token);
                var routerRejected = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
                var error = ExtractError(errorBody);
                if (sendsCacheKey && error.Contains("prompt_cache_key", StringComparison.OrdinalIgnoreCase))
                {
                    _cacheKeyRejected[request.Model] = true;
                    logger.LogInformation("Provider model {Model} menolak prompt_cache_key; model ini dipanggil tanpa kunci cache.", request.Model);
                    return await SendChatAsync(settings, request, onDelta, cancellationToken);
                }
                if (!routerRejected && IsProviderLoginError(error))
                {
                    throw new NineRouterException(ProviderLoginMessage(request.Model, error, settings.NineRouterBaseUrl), (int)response.StatusCode)
                    {
                        IsProviderLoginRequired = true
                    };
                }

                var hint = routerRejected
                    ? " — " + (string.IsNullOrEmpty(settings.NineRouterApiKey) ? KeyMissingMessage : KeyRejectedMessage)
                    : string.Empty;
                throw new NineRouterException(
                    Lang.T($"9router HTTP {(int)response.StatusCode} untuk model '{request.Model}': {error}{hint}", $"9router HTTP {(int)response.StatusCode} for model '{request.Model}': {error}{hint}"),
                    (int)response.StatusCode,
                    retryAfter: RetryAfterOf(response));
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                return EnsureReply(request.Model, await ReadStreamAsync(response, onDelta, () => stall.CancelAfter(_stallTimeout), stall.Token));

            var json = await response.Content.ReadAsStringAsync(stall.Token);
            var result = EnsureReply(request.Model, ParseCompletion(json));
            if (onDelta is not null && result.Content.Length > 0) await onDelta(result.Content);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && !timeout.IsCancellationRequested)
        {
            throw new NineRouterException(Lang.T($"Model '{request.Model}' tidak mengirim data selama {_stallTimeout.TotalSeconds:0} detik; koneksi dianggap putus.", $"Model '{request.Model}' sent no data for {_stallTimeout.TotalSeconds:0} seconds; the connection is treated as dropped."), 408);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NineRouterException(Lang.T($"Model '{request.Model}' tidak merespons dalam {settings.RequestTimeoutSeconds} detik.", $"Model '{request.Model}' did not respond within {settings.RequestTimeoutSeconds} seconds."), 408);
        }
        catch (HttpRequestException ex)
        {
            throw new NineRouterException(Lang.T($"9router tidak dapat dihubungi di {settings.NineRouterBaseUrl}: {ex.Message}", $"9router cannot be reached at {settings.NineRouterBaseUrl}: {ex.Message}"), null, ex);
        }
    }
}
