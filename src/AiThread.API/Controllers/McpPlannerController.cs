using System.Text.Json;
using System.Text.Json.Nodes;
using AiThread.Service.Planner;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

// Minimal MCP server (streamable HTTP transport, JSON responses only) for a running Claude Code CLI planner.
// The random token in the URL identifies the planner run and is its only credential; it dies with the run.
// Supported: initialize, ping, tools/list, tools/call, and notifications (acknowledged with 202).
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public class McpPlannerController(PlannerSessionRegistry sessions) : ControllerBase
{
    private const string FallbackProtocolVersion = "2025-06-18";

    [HttpPost("mcp/planner/{token}")]
    public async Task<IActionResult> Post(string token, CancellationToken cancellationToken)
    {
        if (!sessions.TryGet(token, out var session)) return NotFound();

        JsonNode? body;
        try
        {
            body = await JsonNode.ParseAsync(Request.Body, cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            return JsonRpc(Error(null, -32700, "Parse error"));
        }

        // Older protocol revisions allow batches.
        if (body is JsonArray batch)
        {
            var responses = new JsonArray();
            foreach (var item in batch)
                if (await HandleAsync(item as JsonObject, session, cancellationToken) is { } response)
                    responses.Add(response);
            return responses.Count == 0 ? StatusCode(StatusCodes.Status202Accepted) : JsonRpc(responses);
        }

        var single = await HandleAsync(body as JsonObject, session, cancellationToken);
        return single is null ? StatusCode(StatusCodes.Status202Accepted) : JsonRpc(single);
    }

    // No server-initiated stream and no explicit session teardown.
    [HttpGet("mcp/planner/{token}")]
    [HttpDelete("mcp/planner/{token}")]
    public IActionResult NotSupported() => StatusCode(StatusCodes.Status405MethodNotAllowed);

    private static async Task<JsonObject?> HandleAsync(JsonObject? message, PlannerMcpSession session, CancellationToken cancellationToken)
    {
        if (message is null) return Error(null, -32600, "Invalid request");

        var id = message["id"]?.DeepClone();
        var method = message["method"] is JsonValue m && m.TryGetValue<string>(out var name) ? name : null;

        // Notifications (no id) and responses to server requests need no answer.
        if (id is null) return null;
        if (method is null) return Error(id, -32600, "Invalid request");

        var parameters = message["params"] as JsonObject;
        switch (method)
        {
            case "initialize":
                var requested = parameters?["protocolVersion"] is JsonValue v && v.TryGetValue<string>(out var version) ? version : null;
                return Result(id, new JsonObject
                {
                    ["protocolVersion"] = requested ?? FallbackProtocolVersion,
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
                    ["serverInfo"] = new JsonObject { ["name"] = ClaudeCliPlanner.McpServerName, ["version"] = "1.0.0" },
                    ["instructions"] = "AiThread planner tools: project memory lookup and submit_plan."
                });

            case "ping":
                return Result(id, new JsonObject());

            case "tools/list":
                return Result(id, new JsonObject { ["tools"] = session.Tools.DeepClone() });

            case "tools/call":
                var tool = parameters?["name"] is JsonValue t && t.TryGetValue<string>(out var toolName) ? toolName : null;
                if (tool is null) return Error(id, -32602, "Missing tool name");

                var arguments = parameters?["arguments"]?.ToJsonString() ?? "{}";
                var outcome = await session.InvokeAsync(tool, arguments, cancellationToken);
                return Result(id, new JsonObject
                {
                    ["content"] = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = outcome.Text } },
                    ["isError"] = outcome.IsError
                });

            default:
                return Error(id, -32601, $"Method not found: {method}");
        }
    }

    private static JsonObject Result(JsonNode id, JsonObject result) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };

    private static JsonObject Error(JsonNode? id, int code, string message) =>
        new() { ["jsonrpc"] = "2.0", ["id"] = id, ["error"] = new JsonObject { ["code"] = code, ["message"] = message } };

    private ContentResult JsonRpc(JsonNode node) => Content(node.ToJsonString(), "application/json");
}
