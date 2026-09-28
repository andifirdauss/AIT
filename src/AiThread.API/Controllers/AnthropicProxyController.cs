using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.API.Infrastructure;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AiThread.API.Controllers;

// Anthropic-compatible surface that Claude Code talks to once ANTHROPIC_BASE_URL points here.
// Main agent turns from sessions inside a .sln/.slnx project are orchestrated (planner -> executors -> wrapper);
// everything else, including sessions outside any solution, is forwarded to 9router untouched.
[ApiController]
public class AnthropicProxyController(
    ISettingService settingService,
    INineRouterService router,
    INodeService nodeService,
    IProjectService projectService,
    IThreadOrchestrator orchestrator,
    IActivityLogService activityLog,
    ILogger<AnthropicProxyController> logger) : ControllerBase
{
    private static readonly string[] ForwardedHeaders = ["anthropic-version", "anthropic-beta"];
    private static readonly string[] InspectedHeaders =
        [ClaudeRequestInspector.SolutionHeader, ClaudeRequestInspector.WorkingDirectoryHeader, "x-claude-code-session-id"];

    [HttpPost("v1/messages")]
    [HttpPost("v1/v1/messages")]
    public async Task Messages(CancellationToken cancellationToken)
    {
        var raw = await ReadBodyAsync(cancellationToken);
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        if (!IsAuthorized(settings))
        {
            await WriteErrorAsync(StatusCodes.Status401Unauthorized, "authentication_error", "Invalid AiThread proxy key (ANTHROPIC_AUTH_TOKEN).");
            return;
        }

        if (!TryParse(raw, out var body))
        {
            await WriteErrorAsync(StatusCodes.Status400BadRequest, "invalid_request_error", "Request body must be a JSON object.");
            return;
        }

        var info = ClaudeRequestInspector.Inspect(body, InspectedHeaderValues(), raw.Length);
        var reason = settings.OrchestrationEnabled ? info.Reason : "orchestration disabled";

        if (settings.OrchestrationEnabled && info.IsOrchestratable)
        {
            var project = await ResolveSessionProjectAsync(info, cancellationToken);
            if (project is not null)
            {
                await OrchestrateAsync(info, project, cancellationToken);
                return;
            }
            reason = "session outside a .sln/.slnx project";
        }

        await PassthroughAsync(body, reason, settings, "/v1/messages" + Request.QueryString, cancellationToken);
    }

    [HttpPost("v1/messages/count_tokens")]
    [HttpPost("v1/v1/messages/count_tokens")]
    public async Task CountTokens(CancellationToken cancellationToken)
    {
        var raw = await ReadBodyAsync(cancellationToken);
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        if (!IsAuthorized(settings))
        {
            await WriteErrorAsync(StatusCodes.Status401Unauthorized, "authentication_error", "Invalid AiThread proxy key (ANTHROPIC_AUTH_TOKEN).");
            return;
        }

        if (TryParse(raw, out var body) && await PassthroughModelAsync(settings, cancellationToken) is { } model)
        {
            body["model"] = model;
            try
            {
                using var upstream = await router.ForwardAnthropicAsync("/v1/messages/count_tokens", body.ToJsonString(), ForwardHeaderValues(), cancellationToken);
                if (upstream.IsSuccessStatusCode)
                {
                    Response.ContentType = "application/json";
                    await Response.WriteAsync(await upstream.Content.ReadAsStringAsync(cancellationToken), cancellationToken);
                    return;
                }
            }
            catch (NineRouterException ex)
            {
                logger.LogDebug(ex, "count_tokens passthrough failed; estimating.");
            }
        }

        // Rough estimate (~4 characters per token) keeps Claude Code's context accounting working offline.
        Response.ContentType = "application/json";
        await Response.WriteAsync(JsonSerializer.Serialize(new { input_tokens = Math.Max(1, raw.Length / 4) }), cancellationToken);
    }

    [HttpGet("v1/models")]
    public async Task<IActionResult> Models(CancellationToken cancellationToken)
    {
        IReadOnlyList<Common.DTOs.RouterModelDto> models;
        try
        {
            models = await router.GetModelsAsync(false, cancellationToken);
        }
        catch (Exception ex) when (ex is NineRouterException or HttpRequestException)
        {
            models = [];
        }

        var data = models.Select(m => new { type = "model", id = m.Id, display_name = m.Id, created_at = "2025-01-01T00:00:00Z" }).ToList();
        return Ok(new { data, has_more = false, first_id = data.FirstOrDefault()?.id, last_id = data.LastOrDefault()?.id });
    }

    // Null when the session is outside any solution, or when the check itself fails (the prompt then still
    // reaches a model instead of breaking the user's session).
    private async Task<ProjectContext?> ResolveSessionProjectAsync(ClaudeRequestInfo info, CancellationToken cancellationToken)
    {
        try
        {
            return await projectService.ResolveSessionAsync(info.SessionId, info.WorkingDirectory, info.SolutionHint, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not resolve the project of session {SessionId}.", info.SessionId);
            activityLog.Error("proxy", $"Project sesi tidak dapat ditentukan ({ex.Message}); prompt diteruskan tanpa orkestrasi.");
            return null;
        }
    }

    private async Task OrchestrateAsync(ClaudeRequestInfo info, ProjectContext project, CancellationToken cancellationToken)
    {
        var request = new ThreadRequest(
            ThreadSource.ClaudeCode,
            info.Prompt,
            info.PromptContext,
            info.ConversationContext,
            info.WorkingDirectory,
            project.ProjectId,
            info.SessionId);

        if (!info.Stream)
        {
            try
            {
                var result = await orchestrator.RunAsync(request, NullThreadObserver.Instance, cancellationToken);
                await WriteMessageAsync(info.Model, result.FinalText, info.EstimatedInputTokens, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Orchestration failed before the thread started.");
                await WriteErrorAsync(StatusCodes.Status500InternalServerError, "api_error", $"AiThread: {ex.Message}");
            }
            return;
        }

        await using var writer = new AnthropicSseWriter(Response, info.Model);
        await writer.StartAsync(info.EstimatedInputTokens, cancellationToken);
        try
        {
            await orchestrator.RunAsync(request, new SseThreadObserver(writer), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Failures inside a thread are reported by the orchestrator; this covers setup errors (e.g. database down).
            logger.LogError(ex, "Orchestration failed before the thread started.");
            await writer.TextAsync($"❌ AiThread gagal memulai thread: {ex.Message}", cancellationToken);
        }

        if (!cancellationToken.IsCancellationRequested) await writer.FinishAsync(cancellationToken);
    }

    private async Task PassthroughAsync(JsonObject body, string reason, RuntimeSettings settings, string pathAndQuery, CancellationToken cancellationToken)
    {
        var model = await PassthroughModelAsync(settings, cancellationToken);
        if (model is null)
        {
            await WriteErrorAsync(StatusCodes.Status503ServiceUnavailable, "api_error",
                "AiThread: passthrough model belum diatur (menu Setting) dan tidak ada node aktif yang bisa dipakai.");
            return;
        }

        var requestedModel = body["model"]?.ToString();
        body["model"] = model;

        HttpResponseMessage upstream;
        try
        {
            upstream = await router.ForwardAnthropicAsync(pathAndQuery, body.ToJsonString(), ForwardHeaderValues(), cancellationToken);
        }
        catch (NineRouterException ex)
        {
            await WriteErrorAsync(StatusCodes.Status502BadGateway, "api_error", ex.Message);
            return;
        }

        using (upstream)
        {
            Response.StatusCode = (int)upstream.StatusCode;
            Response.ContentType = upstream.Content.Headers.ContentType?.ToString() ?? "application/json";
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            await using var stream = await upstream.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[8192];
            int read;
            while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await Response.Body.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                await Response.Body.FlushAsync(cancellationToken);
            }
        }

        activityLog.Debug("proxy", $"Passthrough ({reason}) {requestedModel} → {model} [{(int)upstream.StatusCode}]");
    }

    // Explicit setting first, then the cheapest configured workers.
    private async Task<string?> PassthroughModelAsync(RuntimeSettings settings, CancellationToken cancellationToken)
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

    private bool IsAuthorized(RuntimeSettings settings)
    {
        if (string.IsNullOrEmpty(settings.ProxyApiKey)) return true;

        var authorization = Request.Headers.Authorization.ToString();
        var presented = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim()
            : Request.Headers["x-api-key"].ToString();

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(settings.ProxyApiKey));
    }

    private async Task<string> ReadBodyAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
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

    private IReadOnlyDictionary<string, string?> InspectedHeaderValues() =>
        InspectedHeaders.ToDictionary(h => h, h => (string?)Request.Headers[h].ToString(), StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, string> ForwardHeaderValues() =>
        ForwardedHeaders
            .Where(h => Request.Headers.ContainsKey(h))
            .ToDictionary(h => h, h => Request.Headers[h].ToString(), StringComparer.OrdinalIgnoreCase);

    private async Task WriteMessageAsync(string model, string text, int inputTokens, CancellationToken cancellationToken)
    {
        Response.ContentType = "application/json";
        await Response.WriteAsync(JsonSerializer.Serialize(new
        {
            id = "msg_aithread_" + Guid.NewGuid().ToString("N")[..24],
            type = "message",
            role = "assistant",
            model,
            content = new[] { new { type = "text", text } },
            stop_reason = "end_turn",
            stop_sequence = (string?)null,
            usage = new { input_tokens = inputTokens, output_tokens = Math.Max(1, text.Length / 4) }
        }), cancellationToken);
    }

    private async Task WriteErrorAsync(int statusCode, string type, string message)
    {
        Response.StatusCode = statusCode;
        Response.ContentType = "application/json";
        await Response.WriteAsync(JsonSerializer.Serialize(new { type = "error", error = new { type, message } }));
    }
}
