using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.API.Infrastructure;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AiThread.API.Controllers;

// Anthropic Messages surface for Claude Code (ANTHROPIC_BASE_URL) and any other Anthropic-format client.
// The orchestrate-or-forward logic is shared by all client protocols in AgentProxyHandler.
[ApiController]
public class AnthropicProxyController(
    AgentProxyHandler handler,
    ISettingService settingService,
    INineRouterService router,
    ILogger<AnthropicProxyController> logger) : ControllerBase
{
    [HttpPost("v1/messages")]
    [HttpPost("v1/v1/messages")]
    public Task Messages(CancellationToken cancellationToken) =>
        handler.HandleAsync(HttpContext, new ProxyCall(ClientProtocol.AnthropicMessages), cancellationToken);

    [HttpPost("v1/messages/count_tokens")]
    [HttpPost("v1/v1/messages/count_tokens")]
    public async Task CountTokens(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var raw = await reader.ReadToEndAsync(cancellationToken);
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        if (!await handler.AuthorizeAsync(Request, cancellationToken))
        {
            await AgentProxyHandler.WriteErrorAsync(Response, ClientProtocol.AnthropicMessages, StatusCodes.Status401Unauthorized, "authentication_error", "Invalid AIT proxy key (ANTHROPIC_AUTH_TOKEN).");
            return;
        }

        if (TryParse(raw, out var body) && await handler.PassthroughModelAsync(settings, cancellationToken) is { } model)
        {
            body["model"] = model;
            try
            {
                using var upstream = await router.ForwardAsync(
                    "/v1/messages/count_tokens", body.ToJsonString(),
                    AgentProxyHandler.ForwardHeaderValues(Request, ClientProtocol.AnthropicMessages), cancellationToken);
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

    // One list serves Anthropic (`data`, `has_more`, `first_id`), OpenAI (`object`, `owned_by`) and clients that read either.
    [HttpGet("v1/models")]
    public async Task<IActionResult> Models(CancellationToken cancellationToken)
    {
        var models = await ListModelsAsync(cancellationToken);
        var data = models.Select(m => new
        {
            type = "model",
            @object = "model",
            id = m.Id,
            display_name = m.Id,
            created_at = "2025-01-01T00:00:00Z",
            created = 1735689600,
            owned_by = m.IsCombo ? "combo" : "aithread"
        }).ToList();
        return Ok(new { @object = "list", data, has_more = false, first_id = data.FirstOrDefault()?.id, last_id = data.LastOrDefault()?.id });
    }

    private async Task<IReadOnlyList<Common.DTOs.RouterModelDto>> ListModelsAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await router.GetModelsAsync(false, cancellationToken);
        }
        catch (Exception ex) when (ex is NineRouterException or HttpRequestException)
        {
            return [];
        }
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
}
