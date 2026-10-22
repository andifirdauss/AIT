using System.Net;
using System.Text.Json.Nodes;
using AIThread.Service.Interfaces;
using AIThread.Service.Orchestration;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Targets of the Claude Code HTTP hooks written by the CLI menu (ClaudeHookConfig). Claude Code posts
// the hook input as JSON; an empty 200 means "nothing to add", and every failure stays non-blocking on its side.
[ApiController]
[Route("api/hooks/claude")]
public class ClaudeHooksController(ISessionHandoff handoff, ISecretGuard secrets, IActivityLogService activityLog) : ControllerBase
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

    // The model saw secrets as placeholders (passthrough masking); an edit or command that carries one gets the real
    // value back before Claude Code runs it. The developer still confirms the call ("ask"), unless the session already
    // accepts it without asking. A placeholder AIT cannot map back stops the call instead of being written as is.
    // Answers only on this machine: the reply holds the secrets.
    [HttpPost("pre-tool-use")]
    public IActionResult PreToolUse([FromBody] JsonObject input)
    {
        if (!IsLocal() || input["tool_input"] is not JsonObject toolInput) return Ok();

        var tool = Str(input["tool_name"]) ?? "tool";
        var updated = toolInput.DeepClone().AsObject();
        var file = Str(updated["file_path"]) ?? Str(updated["notebook_path"]);
        if (file is not null && !Path.IsPathRooted(file) && Str(input["cwd"]) is { } cwd) file = Path.Combine(cwd, file);

        var restore = secrets.RestoreJson(updated, file);
        if (!restore.Complete)
        {
            activityLog.Warn("keamanan", $"{tool} dihentikan: memakai placeholder secret yang tidak dikenali AIT.");
            return Ok(new
            {
                hookSpecificOutput = new
                {
                    hookEventName = "PreToolUse",
                    permissionDecision = "deny",
                    permissionDecisionReason = $"This call contains AIT secret placeholder(s) that AIT cannot map back to a value: {string.Join(", ", restore.Unknown.Distinct())}. " +
                                               "Read the file again (its secrets are masked the same way) and copy the placeholders exactly, or ask the user for the value."
                }
            });
        }
        if (restore.Restored == 0) return Ok();

        activityLog.Info("keamanan", $"{restore.Restored} secret dikembalikan ke {tool} sebelum dijalankan Claude Code.");
        return Ok(new
        {
            hookSpecificOutput = new
            {
                hookEventName = "PreToolUse",
                permissionDecision = AcceptsWithoutAsking(Str(input["permission_mode"]), tool) ? "allow" : "ask",
                permissionDecisionReason = $"AIT mengembalikan {restore.Restored} secret yang disamarkan dari model.",
                updatedInput = updated
            }
        });
    }

    // The permission mode of the session already lets this call run without a prompt.
    private static bool AcceptsWithoutAsking(string? mode, string tool) =>
        mode == "bypassPermissions" || (mode == "acceptEdits" && tool != "Bash");

    private bool IsLocal() =>
        HttpContext.Connection.RemoteIpAddress is not { } address
        || IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
}
