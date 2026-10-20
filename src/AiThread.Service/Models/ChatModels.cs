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

    // Sent as prompt_cache_key: OpenAI routes requests with the same key and prefix to the same cache, and 9router
    // uses it as the session id. Requests sharing a prompt prefix share a key (see CacheKey).
    public string? PromptCacheKey { get; init; }

    // "ait:{thread}:{role}", well below the providers' 64-character limit.
    public static string CacheKey(Guid threadId, string role) => $"ait:{threadId:N}:{role}";
}

public sealed record ToolCall(string Id, string Name, string Arguments);

// CachedTokens is the part of PromptTokens served from the provider's prompt cache (0 when unknown).
public sealed record ChatCompletionResult(
    string Content,
    IReadOnlyList<ToolCall> ToolCalls,
    string? FinishReason,
    int PromptTokens,
    int CompletionTokens,
    int CachedTokens = 0)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}

public static class ChatMessages
{
    public static JsonObject System(string content) => new() { ["role"] = "system", ["content"] = content };

    public static JsonObject User(string content) => new() { ["role"] = "user", ["content"] = content };

    // OpenAI vision shape: content becomes a parts array when images are attached, plain text otherwise
    // (plain text keeps working on models/routes that reject the array form for text-only turns).
    public static JsonObject User(string content, IReadOnlyList<ImageAttachment>? images)
    {
        if (images is not { Count: > 0 }) return User(content);

        var parts = new JsonArray { new JsonObject { ["type"] = "text", ["text"] = content } };
        foreach (var image in images)
        {
            parts.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = $"data:{image.MediaType};base64,{image.Data}" }
            });
        }
        return new JsonObject { ["role"] = "user", ["content"] = parts };
    }

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
