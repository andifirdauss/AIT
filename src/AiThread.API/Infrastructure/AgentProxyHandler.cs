using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;
using AiThread.Service.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace AiThread.API.Infrastructure;

// What a controller knows about the call it received. Gemini carries model and streaming in the URL.
public sealed record ProxyCall(ClientProtocol Protocol, string? Model = null, bool? Stream = null, string? GeminiAction = null);

// The proxy behind every client protocol (Anthropic Messages, OpenAI chat/responses, Gemini). Main agent turns from
// sessions inside a project (a folder with .git or a build manifest) are orchestrated (planner -> executors -> wrapper) and answered in the client's
// own format; everything else is forwarded untouched to the same endpoint of 9router, which translates it for
// whichever provider the passthrough model lives on.
public sealed class AgentProxyHandler(
    ISettingService settingService,
    INineRouterService router,
    INodeService nodeService,
    IProjectService projectService,
    IThreadOrchestrator orchestrator,
    IActivityLogService activityLog,
    ContextKeeper contextKeeper,
    ILogger<AgentProxyHandler> logger)
{
    private static readonly string[] AnthropicForwardedHeaders = ["anthropic-version", "anthropic-beta"];
    private static readonly string[] InspectedHeaders =
        [ClaudeRequestInspector.ProjectHeader, ClaudeRequestInspector.LegacySolutionHeader, ClaudeRequestInspector.WorkingDirectoryHeader, .. ClaudeRequestInspector.SessionHeaders];

    public async Task HandleAsync(HttpContext http, ProxyCall call, CancellationToken cancellationToken)
    {
        var raw = await ReadBodyAsync(http.Request, cancellationToken);
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        if (!IsAuthorized(http.Request, settings))
        {
            await WriteErrorAsync(http.Response, call.Protocol, StatusCodes.Status401Unauthorized, "authentication_error", "Invalid AIT proxy key (ANTHROPIC_AUTH_TOKEN / OPENAI_API_KEY / GEMINI_API_KEY).");
            return;
        }

        if (!TryParse(raw, out var body))
        {
            await WriteErrorAsync(http.Response, call.Protocol, StatusCodes.Status400BadRequest, "invalid_request_error", "Request body must be a JSON object.");
            return;
        }

        var canonical = ProtocolAdapter.ToCanonical(call.Protocol, body, call.Model, call.Stream);
        var info = ClaudeRequestInspector.Inspect(canonical, InspectedHeaderValues(http.Request), raw.Length);

        var reason = settings.OrchestrationEnabled ? info.Reason : "orchestration disabled";

        if (settings.OrchestrationEnabled && info.IsOrchestratable)
        {
            var project = await ResolveSessionProjectAsync(info, cancellationToken);
            if (project is not null)
            {
                await OrchestrateAsync(http, call, info, project, canonical, cancellationToken);
                return;
            }
            reason = "session outside any project (no project marker)";
        }

        await PassthroughAsync(http, call, body, reason, settings, cancellationToken);
    }

    public async Task<bool> AuthorizeAsync(HttpRequest request, CancellationToken cancellationToken) =>
        IsAuthorized(request, await settingService.GetRuntimeAsync(cancellationToken));

    // Null when the session is outside any project, or when the check itself fails (the prompt then still
    // reaches a model instead of breaking the user's session).
    private async Task<ProjectContext?> ResolveSessionProjectAsync(ClaudeRequestInfo info, CancellationToken cancellationToken)
    {
        try
        {
            return await projectService.ResolveSessionAsync(info.SessionId, info.WorkingDirectory, info.ProjectHint, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not resolve the project of session {SessionId}.", info.SessionId);
            activityLog.Error("proxy", $"Project sesi tidak dapat ditentukan ({ex.Message}); prompt diteruskan tanpa orkestrasi.");
            return null;
        }
    }

    private async Task OrchestrateAsync(HttpContext http, ProxyCall call, ClaudeRequestInfo info, ProjectContext project, JsonObject canonical, CancellationToken cancellationToken)
    {
        // With a context pack, only the turns after the thread that wrote it still need to travel as conversation.
        var session = await ResolveSessionAsync(info, project, cancellationToken);
        var conversation = session?.PackMessageIndex is { } packIndex
            ? ClaudeRequestInspector.HistoryAfter(canonical, packIndex)
            : info.ConversationContext;

        var request = new ThreadRequest(
            ThreadSource.ClaudeCode,
            info.Prompt,
            info.PromptContext,
            conversation,
            info.WorkingDirectory,
            project.ProjectId,
            info.SessionId,
            session?.Context,
            info.Images,
            ProxyUrlResolver.Resolve(http));

        if (!info.Stream)
        {
            try
            {
                var result = await orchestrator.RunAsync(request, NullThreadObserver.Instance, cancellationToken);
                http.Response.ContentType = "application/json";
                await http.Response.WriteAsync(
                    JsonSerializer.Serialize(ClientResponses.Message(call.Protocol, info.Model, result.FinalText, info.EstimatedInputTokens)), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Orchestration failed before the thread started.");
                await WriteErrorAsync(http.Response, call.Protocol, StatusCodes.Status500InternalServerError, "api_error", $"AIT: {ex.Message}");
            }
            return;
        }

        await using var writer = ClientResponses.CreateWriter(call.Protocol, http.Response, info.Model);
        await writer.StartAsync(info.EstimatedInputTokens, cancellationToken);
        try
        {
            await orchestrator.RunAsync(request, new SseThreadObserver(writer), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failures inside a thread are reported by the orchestrator; this covers setup errors (e.g. database down).
            logger.LogError(ex, "Orchestration failed before the thread started.");
            await writer.TextAsync($"❌ AIT gagal memulai thread: {ex.Message}", cancellationToken);
        }

        if (!cancellationToken.IsCancellationRequested) await writer.FinishAsync(cancellationToken);
    }

    // A failed lookup only costs continuity: the planner then gets the recent conversation as before.
    private async Task<ResolvedSession?> ResolveSessionAsync(ClaudeRequestInfo info, ProjectContext project, CancellationToken cancellationToken)
    {
        try
        {
            return await contextKeeper.ResolveAsync(project.ProjectId, info.Anchors, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not resolve the context pack of session {SessionId}.", info.SessionId);
            return null;
        }
    }

    private async Task PassthroughAsync(HttpContext http, ProxyCall call, JsonObject body, string reason, RuntimeSettings settings, CancellationToken cancellationToken)
    {
        var model = await PassthroughModelAsync(settings, cancellationToken);
        if (model is null)
        {
            await WriteErrorAsync(http.Response, call.Protocol, StatusCodes.Status503ServiceUnavailable, "api_error",
                "AIT: passthrough model belum diatur (menu Setting) dan tidak ada node aktif yang bisa dipakai.");
            return;
        }

        // Gemini names the model in the URL; the other protocols carry it in the body.
        var requestedModel = call.Protocol == ClientProtocol.GeminiGenerate ? call.Model : body["model"]?.ToString();
        if (call.Protocol != ClientProtocol.GeminiGenerate) body["model"] = model;

        var path = ProtocolAdapter.RouterPath(call.Protocol, model, call.GeminiAction, ForwardQuery(http.Request));

        HttpResponseMessage upstream;
        try
        {
            upstream = await router.ForwardAsync(path, body.ToJsonString(), ForwardHeaderValues(http.Request, call.Protocol), cancellationToken);
        }
        catch (NineRouterException ex)
        {
            await WriteErrorAsync(http.Response, call.Protocol, StatusCodes.Status502BadGateway, "api_error", ex.Message);
            return;
        }

        using (upstream)
        {
            http.Response.StatusCode = (int)upstream.StatusCode;
            http.Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json";
            http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            await using var stream = await upstream.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await http.Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                await http.Response.Body.FlushAsync(cancellationToken);
            }
        }

        activityLog.Debug("proxy", $"Passthrough ({reason}) [{call.Protocol}] {requestedModel} → {model} [{(int)upstream.StatusCode}]");
    }

    // Explicit setting first, then the cheapest configured workers.
    public async Task<string?> PassthroughModelAsync(RuntimeSettings settings, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.PassthroughModel)) return settings.PassthroughModel;
        try
        {
            return (await nodeService.GetActiveAsync(NodeRole.Wrapper, cancellationToken)).FirstOrDefault()?.Model
                   ?? (await nodeService.GetActiveAsync(NodeRole.Executor, cancellationToken)).FirstOrDefault()?.Model;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not resolve a passthrough model.");
            return null;
        }
    }

    // The proxy key arrives however the client's SDK sends an API key: Bearer (Anthropic, OpenAI), x-api-key
    // (Anthropic), x-goog-api-key or ?key= (Gemini).
    private static bool IsAuthorized(HttpRequest request, RuntimeSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ProxyApiKey)) return true;

        var authorization = request.Headers.Authorization.ToString();
        var presented = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim()
            : FirstNonEmpty(request.Headers["x-api-key"].ToString(), request.Headers["x-goog-api-key"].ToString(), request.Query["key"].ToString());

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(settings.ProxyApiKey));
    }

    private static string FirstNonEmpty(params string[] values) => values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? string.Empty;

    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    private static bool TryParse(string raw, out JsonObject body)
    {
        body = null!;
        try
        {
            if (JsonNode.Parse(raw) is not JsonObject parsed) return false;
            body = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyDictionary<string, string?> InspectedHeaderValues(HttpRequest request) =>
        InspectedHeaders.ToDictionary(h => h, h => (string?)request.Headers[h].ToString(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, string> ForwardHeaderValues(HttpRequest request, ClientProtocol protocol) =>
        protocol != ClientProtocol.AnthropicMessages
            ? new Dictionary<string, string>()
            : AnthropicForwardedHeaders
                .Where(h => request.Headers.ContainsKey(h))
                .ToDictionary(h => h, h => request.Headers[h].ToString(), StringComparer.OrdinalIgnoreCase);

    // The client's query string minus its credential: `?key=` holds the AiThread proxy key for Gemini clients.
    private static string ForwardQuery(HttpRequest request)
    {
        var pairs = request.Query
            .Where(q => !q.Key.Equals("key", StringComparison.OrdinalIgnoreCase))
            .SelectMany(q => q.Value.Select(v => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(v ?? string.Empty)}"))
            .ToList();
        return pairs.Count == 0 ? string.Empty : "?" + string.Join('&', pairs);
    }

    public static async Task WriteErrorAsync(HttpResponse response, ClientProtocol protocol, int statusCode, string type, string message)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        await response.WriteAsync(JsonSerializer.Serialize(ClientResponses.Error(protocol, statusCode, type, message)));
    }
}
