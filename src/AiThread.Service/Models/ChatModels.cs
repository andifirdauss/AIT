using System.Text.Json.Nodes;

namespace AiThread.Service.Models;

// OpenAI-compatible chat completion request sent to 9router.
public sealed class ChatCompletionRequest
{
    public required string Model { get; init; }
    public required JsonArray Messages { get; init; }
    public JsonArray? Tools { get; init; }
    public double? Temperature { get; init; }
    public int? MaxTokens { get; init; }
}

public sealed record ToolCall(string Id, string Name, string Arguments);

public sealed record ChatCompletionResult(
    string Content,
    IReadOnlyList<ToolCall> ToolCalls,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}

public static class ChatMessages
{
    public static JsonObject System(string content) => new() { ["role"] = "system", ["content"] = content };

    public static JsonObject User(string content) => new() { ["role"] = "user", ["content"] = content };

    public static JsonObject Assistant(string? content, IReadOnlyList<ToolCall>? toolCalls = null)
    {
        var message = new JsonObject { ["role"] = "assistant", ["content"] = content };
        if (toolCalls is { Count: > 0 })
        {
            var calls = new JsonArray();
            foreach (var call in toolCalls)
            {
                calls.Add(new JsonObject
                {
                    ["id"] = call.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments }
                });
            }
            message["tool_calls"] = calls;
        }
        return message;
    }

    public static JsonObject Tool(string toolCallId, string content) =>
        new() { ["role"] = "tool", ["tool_call_id"] = toolCallId, ["content"] = content };

    public static JsonObject Function(string name, string description, JsonObject parameters) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["parameters"] = parameters
        }
    };
}
