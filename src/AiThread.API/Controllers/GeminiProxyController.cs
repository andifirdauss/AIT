using AiThread.API.Infrastructure;
using AiThread.Service.Interfaces;
using AiThread.Service.Models;
using AiThread.Service.Proxy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

// Gemini native surface (Gemini CLI with GOOGLE_GEMINI_BASE_URL): POST /v1beta/models/{model}:generateContent and
// :streamGenerateContent. The model and the streaming choice live in the URL, not the body.
[ApiController]
public class GeminiProxyController(AgentProxyHandler handler, INineRouterService router) : ControllerBase
{
    [HttpPost("v1beta/models/{**rest}")]
    public async Task Generate(string rest, CancellationToken cancellationToken)
    {
        var separator = rest.LastIndexOf(':');
        if (separator <= 0)
        {
            await AgentProxyHandler.WriteErrorAsync(Response, ClientProtocol.GeminiGenerate, StatusCodes.Status404NotFound, "not_found",
                "Expected /v1beta/models/{model}:generateContent or :streamGenerateContent.");
            return;
        }

        var model = Uri.UnescapeDataString(rest[..separator]);
        var action = rest[(separator + 1)..];
        if (action is not ("generateContent" or "streamGenerateContent"))
        {
            // countTokens and friends have no orchestrated form; they are not proxied.
            await AgentProxyHandler.WriteErrorAsync(Response, ClientProtocol.GeminiGenerate, StatusCodes.Status404NotFound, "not_found",
                $"AiThread hanya mendukung generateContent dan streamGenerateContent (diminta: {action}).");
            return;
        }

        await handler.HandleAsync(
            HttpContext,
            new ProxyCall(ClientProtocol.GeminiGenerate, model, action == "streamGenerateContent", action),
            cancellationToken);
    }

    [HttpGet("v1beta/models")]
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

        return Ok(new
        {
            models = models.Select(m => new
            {
                name = "models/" + m.Id,
                displayName = m.Id,
                supportedGenerationMethods = new[] { "generateContent", "streamGenerateContent" }
            })
        });
    }
}
