using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Implementations;

// Client for 9router (https://github.com/decolua/9router):
//   GET  /api/health             -> {"ok":true}, no auth
//   GET  /v1/models              -> OpenAI list; combos have owned_by = "combo", no auth
//   POST /v1/chat/completions    -> OpenAI chat, Bearer key when "require API key" is on
//   POST /v1/messages            -> Anthropic Messages API (Claude Code passthrough)
//   POST /v1/responses           -> OpenAI Responses API (Codex passthrough)
//   POST /v1beta/models/{m}:generateContent | :streamGenerateContent -> Gemini native (Gemini CLI passthrough)
public sealed class NineRouterService(
    IHttpClientFactory httpClientFactory,
    ISettingService settingService,
    ILogger<NineRouterService> logger) : INineRouterService
{
    public const string HttpClientName = "NineRouter";
    public const string KeyMissingMessage = "9router mewajibkan API key, tetapi belum ada API key tersimpan. Isi API key di menu Setting lalu klik Simpan.";
    public const string KeyRejectedMessage = "API key ditolak oleh 9router. Periksa atau ganti API key di menu Setting.";

    private static readonly TimeSpan ModelCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan KeyCheckCacheDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim _modelLock = new(1, 1);
    private ModelCache? _modelCache;
    private KeyCheck? _keyCheck;

    public RouterStatusDto? LastStatus { get; private set; }

    public async Task<RouterStatusDto> CheckStatusAsync(CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var status = await ProbeAsync(settings.NineRouterBaseUrl, settings.NineRouterApiKey, useCache: true, cancellationToken);
        LastStatus = status;
        return status;
    }

    public async Task<RouterStatusDto> TestConnectionAsync(string? baseUrl, string? apiKey, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var url = SettingService.NormalizeRouterUrl(string.IsNullOrWhiteSpace(baseUrl) ? settings.NineRouterBaseUrl : baseUrl);
        var key = string.IsNullOrWhiteSpace(apiKey) ? settings.NineRouterApiKey : apiKey.Trim();
        return await ProbeAsync(url, key, useCache: false, cancellationToken);
    }

    public async Task<IReadOnlyList<RouterModelDto>> GetModelsAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        return await GetModelsCoreAsync(settings.NineRouterBaseUrl, refresh, cancellationToken);
    }

    public async Task<ChatCompletionResult> ChatAsync(
        ChatCompletionRequest request,
        Func<string, Task>? onDelta = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var emitted = false;
        Func<string, Task>? trackedDelta = onDelta is null
            ? null
            : async delta =>
            {
                emitted = true;
                await onDelta(delta);
            };

        const int maxAttempts = 3;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await SendChatAsync(settings, request, trackedDelta, cancellationToken);
            }
            catch (NineRouterException ex) when (ex.IsTransient && attempt < maxAttempts && !emitted && !cancellationToken.IsCancellationRequested
                                                 && RetryDelay(ex, attempt) is { } delay)
            {
                logger.LogWarning("9router call to {Model} failed (attempt {Attempt}), retrying in {Delay}s: {Message}",
                    request.Model, attempt, delay.TotalSeconds, ex.Message);
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    // Null means "do not retry here": the upstream asked for a longer wait than is worth blocking on,
    // so the caller should fail over to another model instead.
    public static TimeSpan? RetryDelay(NineRouterException ex, int attempt)
    {
        if (ex.RetryAfter is { } requested)
            return requested <= MaxRetryAfter ? requested : null;

        // Rate limits usually clear on a scale of seconds to a minute; other transient errors retry sooner.
        var baseSeconds = ex.IsRateLimited ? 5 * attempt : 2 * attempt;
        return TimeSpan.FromSeconds(baseSeconds + Random.Shared.NextDouble() * baseSeconds / 2);
    }

    public async Task<HttpResponseMessage> ForwardAsync(
        string pathAndQuery,
        string body,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        var message = new HttpRequestMessage(HttpMethod.Post, Combine(settings.NineRouterBaseUrl, pathAndQuery))
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        foreach (var (name, value) in headers) message.Headers.TryAddWithoutValidation(name, value);
        if (!string.IsNullOrEmpty(settings.NineRouterApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.NineRouterApiKey);

        try
        {
            return await Client().SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new NineRouterException($"9router tidak dapat dihubungi di {settings.NineRouterBaseUrl}: {ex.Message}", null, ex);
        }
    }

    private async Task<RouterStatusDto> ProbeAsync(string baseUrl, string? apiKey, bool useCache, CancellationToken cancellationToken)
    {
        var hasKey = !string.IsNullOrEmpty(apiKey);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            using (var health = await Client().GetAsync(Combine(baseUrl, "/api/health"), timeout.Token))
            {
                stopwatch.Stop();
                // Older builds or reverse proxies may not expose /api/health; the model list below still proves liveness.
                if (!health.IsSuccessStatusCode && health.StatusCode != HttpStatusCode.NotFound)
                    return Offline(baseUrl, hasKey, $"Health check HTTP {(int)health.StatusCode}");
            }

            var models = await GetModelsCoreAsync(baseUrl, refresh: !useCache, cancellationToken);

            // Health and the model list are public, so "online" says nothing about auth. ApiKeyValid answers
            // "will chat requests authenticate?": false also when no key is stored but 9router requires one.
            var accepted = await IsAuthenticationAcceptedAsync(baseUrl, hasKey ? apiKey : null, useCache, cancellationToken);
            bool? keyValid = hasKey ? accepted : accepted ? null : false;

            return new RouterStatusDto(
                true,
                baseUrl,
                stopwatch.ElapsedMilliseconds,
                models.Count,
                models.Count(m => m.IsCombo),
                hasKey,
                keyValid,
                accepted ? null : hasKey ? KeyRejectedMessage : KeyMissingMessage,
                DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException or NineRouterException or JsonException
                                       || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return Offline(baseUrl, hasKey, ex is OperationCanceledException ? "Timeout (5 detik)" : ex.Message);
        }
    }

    private static RouterStatusDto Offline(string baseUrl, bool hasKey, string error) =>
        new(false, baseUrl, null, 0, 0, hasKey, null, error, DateTime.UtcNow);

    // 9router authenticates before validating the body, so an empty request tells a rejected or missing key (401)
    // apart from an accepted one (400 "Missing model") without spending any tokens. A null key checks whether
    // 9router requires one at all (its "require API key" option).
    private async Task<bool> IsAuthenticationAcceptedAsync(string baseUrl, string? apiKey, bool useCache, CancellationToken cancellationToken)
    {
        var cached = _keyCheck;
        if (useCache && cached is not null && cached.BaseUrl == baseUrl && cached.Key == apiKey && DateTime.UtcNow - cached.CheckedAt < KeyCheckCacheDuration)
            return cached.Valid;

        using var request = new HttpRequestMessage(HttpMethod.Post, Combine(baseUrl, "/v1/chat/completions"))
        {
            Content = new StringContent("{\"messages\":[]}", Encoding.UTF8, "application/json")
        };
        if (apiKey is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await Client().SendAsync(request, timeout.Token);
        var valid = response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);

        _keyCheck = new KeyCheck(baseUrl, apiKey, valid, DateTime.UtcNow);
        return valid;
    }

    private async Task<IReadOnlyList<RouterModelDto>> GetModelsCoreAsync(string baseUrl, bool refresh, CancellationToken cancellationToken)
    {
        var cache = _modelCache;
        if (!refresh && cache is not null && cache.BaseUrl == baseUrl && DateTime.UtcNow - cache.LoadedAt < ModelCacheDuration)
            return cache.Models;

        await _modelLock.WaitAsync(cancellationToken);
        try
        {
            cache = _modelCache;
            if (!refresh && cache is not null && cache.BaseUrl == baseUrl && DateTime.UtcNow - cache.LoadedAt < ModelCacheDuration)
                return cache.Models;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await Client().GetAsync(Combine(baseUrl, "/v1/models"), timeout.Token);
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new NineRouterException($"GET /v1/models HTTP {(int)response.StatusCode}: {ExtractError(json)}", (int)response.StatusCode);

            var models = ParseModels(json);
            _modelCache = new ModelCache(baseUrl, models, DateTime.UtcNow);
            return models;
        }
        finally
        {
            _modelLock.Release();
        }
    }

    private static List<RouterModelDto> ParseModels(string json)
    {
        var models = new List<RouterModelDto>();
        if (Property(JsonNode.Parse(json), "data") is not JsonArray data) return models;

        foreach (var item in data)
        {
            var id = GetString(item?["id"]);
            if (string.IsNullOrWhiteSpace(id)) continue;

            // Search/fetch pseudo-models are not chat models.
            var kind = GetString(item?["kind"]);
            if (kind is "webSearch" or "webFetch") continue;

            var ownedBy = GetString(item?["owned_by"]) ?? string.Empty;
            models.Add(new RouterModelDto(
                id,
                ownedBy,
                string.Equals(ownedBy, "combo", StringComparison.OrdinalIgnoreCase),
                GetInt(item?["context_length"]),
                GetInt(item?["max_completion_tokens"])));
        }

        return models
            .OrderByDescending(m => m.IsCombo)
            .ThenBy(m => m.OwnedBy, StringComparer.OrdinalIgnoreCase)
            .ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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

        using var message = new HttpRequestMessage(HttpMethod.Post, Combine(settings.NineRouterBaseUrl, "/v1/chat/completions"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        message.Headers.Accept.ParseAdd("text/event-stream");
        if (!string.IsNullOrEmpty(settings.NineRouterApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.NineRouterApiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));

        try
        {
            using var response = await Client().SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(timeout.Token);
                var hint = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                    ? " — " + (string.IsNullOrEmpty(settings.NineRouterApiKey) ? KeyMissingMessage : KeyRejectedMessage)
                    : string.Empty;
                throw new NineRouterException(
                    $"9router HTTP {(int)response.StatusCode} untuk model '{request.Model}': {ExtractError(errorBody)}{hint}",
                    (int)response.StatusCode,
                    retryAfter: RetryAfterOf(response));
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
                return await ReadStreamAsync(response, onDelta, timeout.Token);

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var result = ParseCompletion(json);
            if (onDelta is not null && result.Content.Length > 0) await onDelta(result.Content);
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new NineRouterException($"Model '{request.Model}' tidak merespons dalam {settings.RequestTimeoutSeconds} detik.", 408);
        }
        catch (HttpRequestException ex)
        {
            throw new NineRouterException($"9router tidak dapat dihubungi di {settings.NineRouterBaseUrl}: {ex.Message}", null, ex);
        }
    }

    private static async Task<ChatCompletionResult> ReadStreamAsync(
        HttpResponseMessage response,
        Func<string, Task>? onDelta,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var content = new StringBuilder();
        var toolCalls = new SortedDictionary<int, ToolCallBuilder>();
        string? finishReason = null;
        int promptTokens = 0, completionTokens = 0;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") break;

            JsonNode? chunk;
            try
            {
                chunk = JsonNode.Parse(data);
            }
            catch (JsonException)
            {
                continue;
            }
            if (chunk is null) continue;

            if (chunk["error"] is { } error)
                throw new NineRouterException(GetString(error["message"]) ?? error.ToJsonString(), GetInt(error["code"]) ?? 502);

            if (chunk["usage"] is JsonObject usage)
            {
                promptTokens = GetInt(usage["prompt_tokens"]) ?? promptTokens;
                completionTokens = GetInt(usage["completion_tokens"]) ?? completionTokens;
            }

            if (chunk["choices"] is not JsonArray { Count: > 0 } choices) continue;
            var choice = choices[0];
            var delta = choice?["delta"] ?? choice?["message"];

            if (GetString(delta?["content"]) is { Length: > 0 } text)
            {
                content.Append(text);
                if (onDelta is not null) await onDelta(text);
            }

            if (delta?["tool_calls"] is JsonArray callDeltas)
            {
                foreach (var callDelta in callDeltas)
                {
                    if (callDelta is null) continue;
                    var id = GetString(callDelta["id"]);
                    var index = ResolveToolCallIndex(toolCalls, GetInt(callDelta["index"]), id);
                    if (!toolCalls.TryGetValue(index, out var builder))
                    {
                        builder = new ToolCallBuilder();
                        toolCalls[index] = builder;
                    }

                    if (!string.IsNullOrEmpty(id)) builder.Id = id;
                    if (GetString(callDelta["function"]?["name"]) is { Length: > 0 } name) builder.Name.Append(name);
                    if (GetString(callDelta["function"]?["arguments"]) is { Length: > 0 } arguments) builder.Arguments.Append(arguments);
                }
            }

            if (GetString(choice?["finish_reason"]) is { Length: > 0 } finish) finishReason = finish;
        }

        return new ChatCompletionResult(
            content.ToString(),
            toolCalls.Values.Select((b, i) => b.Build(i)).Where(c => c.Name.Length > 0).ToList(),
            finishReason,
            promptTokens,
            completionTokens);
    }

    // Most providers send an explicit index; some translated streams only send ids, or neither on continuation chunks.
    private static int ResolveToolCallIndex(SortedDictionary<int, ToolCallBuilder> toolCalls, int? explicitIndex, string? id)
    {
        if (explicitIndex is { } index) return index;
        if (id is not null)
        {
            foreach (var (key, builder) in toolCalls)
                if (builder.Id == id) return key;
            return toolCalls.Count == 0 ? 0 : toolCalls.Keys.Max() + 1;
        }
        return toolCalls.Count == 0 ? 0 : toolCalls.Keys.Max();
    }

    private static ChatCompletionResult ParseCompletion(string json)
    {
        var root = JsonNode.Parse(json) ?? throw new NineRouterException("Respons 9router kosong.");
        if (root["error"] is { } error)
            throw new NineRouterException(GetString(error["message"]) ?? error.ToJsonString(), 502);

        var choice = root["choices"]?[0];
        var message = choice?["message"];
        var toolCalls = new List<ToolCall>();
        if (message?["tool_calls"] is JsonArray calls)
        {
            var i = 0;
            foreach (var call in calls)
            {
                var name = GetString(call?["function"]?["name"]);
                if (string.IsNullOrEmpty(name)) continue;
                toolCalls.Add(new ToolCall(
                    GetString(call?["id"]) ?? $"call_{i}",
                    name,
                    GetString(call?["function"]?["arguments"]) ?? "{}"));
                i++;
            }
        }

        return new ChatCompletionResult(
            GetString(message?["content"]) ?? string.Empty,
            toolCalls,
            GetString(choice?["finish_reason"]),
            GetInt(root["usage"]?["prompt_tokens"]) ?? 0,
            GetInt(root["usage"]?["completion_tokens"]) ?? 0);
    }

    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(no body)";
        try
        {
            // The body may be any JSON shape ({"error":"..."}, an array, a bare string), so never index a non-object.
            var node = JsonNode.Parse(body);
            var error = Property(node, "error");
            var message = GetString(Property(error, "message")) ?? GetString(Property(node, "message")) ?? GetString(error) ?? GetString(node);
            if (!string.IsNullOrWhiteSpace(message)) return message;
        }
        catch (JsonException)
        {
            // Not JSON — fall through to the raw text.
        }
        return body.Length > 500 ? body[..500] : body;
    }

    private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        if (header?.Date is { } date)
        {
            var wait = date - DateTimeOffset.UtcNow;
            return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
        }
        return null;
    }

    private static JsonNode? Property(JsonNode? node, string name) =>
        node is JsonObject obj ? obj[name] : null;

    private HttpClient Client() => httpClientFactory.CreateClient(HttpClientName);

    private static string Combine(string baseUrl, string path) => baseUrl.TrimEnd('/') + "/" + path.TrimStart('/');

    private static string? GetString(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static int? GetInt(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<int>(out var i)) return i;
        if (value.TryGetValue<long>(out var l)) return (int)Math.Clamp(l, int.MinValue, int.MaxValue);
        if (value.TryGetValue<double>(out var d)) return (int)d;
        if (value.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
        return null;
    }

    private sealed class ToolCallBuilder
    {
        public string? Id { get; set; }
        public StringBuilder Name { get; } = new();
        public StringBuilder Arguments { get; } = new();

        public ToolCall Build(int index) => new(
            string.IsNullOrEmpty(Id) ? $"call_{index}_{Guid.NewGuid():N}"[..24] : Id,
            Name.ToString(),
            Arguments.Length == 0 ? "{}" : Arguments.ToString());
    }

    private sealed record ModelCache(string BaseUrl, IReadOnlyList<RouterModelDto> Models, DateTime LoadedAt);

    private sealed record KeyCheck(string BaseUrl, string? Key, bool Valid, DateTime CheckedAt);
}
