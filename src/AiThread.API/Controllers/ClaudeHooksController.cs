using System.Text.Json.Nodes;
using AiThread.Service.Interfaces;
using AiThread.Service.Orchestration;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

// Targets of the Claude Code HTTP hooks written by the CLI menu (ClaudeHookConfig). Claude Code posts
// the hook input as JSON; an empty 200 means "nothing to add", and every failure stays non-blocking on its side.
[ApiController]
[Route("api/hooks/claude")]
public class ClaudeHooksController(ISessionHandoff handoff) : ControllerBase
{
    [HttpPost("session-end")]
    public async Task<IActionResult> SessionEnd([FromBody] JsonObject input, CancellationToken cancellationToken)
    {
        if (Str(input["reason"]) == "clear"
            && Str(input["session_id"]) is { } sessionId
            && Str(input["transcript_path"]) is { } transcriptPath
            && Str(input["cwd"]) is { } workingDirectory)
        {
            await handoff.BeginAsync(sessionId, transcriptPath, workingDirectory, cancellationToken);
        }
        return Ok();
    }

    [HttpPost("session-start")]
    public async Task<IActionResult> SessionStart([FromBody] JsonObject input, CancellationToken cancellationToken)
    {
        if (Str(input["source"]) != "clear" || Str(input["session_id"]) is not { } sessionId || Str(input["cwd"]) is not { } workingDirectory)
            return Ok();

        var context = await handoff.TakeAsync(sessionId, workingDirectory, cancellationToken);
        return context is null
            ? Ok()
            : Ok(new { hookSpecificOutput = new { hookEventName = "SessionStart", additionalContext = context } });
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
