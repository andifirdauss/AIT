using AIThread.API.Infrastructure;
using AIThread.Service.Proxy;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// OpenAI-format surface: Chat Completions (Cursor, Continue, opencode, oh-my-pi...) and Responses (Codex CLI).
// GET /v1/models is shared with the Anthropic controller.
[ApiController]
public class OpenAiProxyController(AgentProxyHandler handler) : ControllerBase
{
    [HttpPost("v1/chat/completions")]
    public Task ChatCompletions(CancellationToken cancellationToken) =>
        handler.HandleAsync(HttpContext, new ProxyCall(ClientProtocol.OpenAiChat), cancellationToken);

    [HttpPost("v1/responses")]
    public Task Responses(CancellationToken cancellationToken) =>
        handler.HandleAsync(HttpContext, new ProxyCall(ClientProtocol.OpenAiResponses), cancellationToken);
}
