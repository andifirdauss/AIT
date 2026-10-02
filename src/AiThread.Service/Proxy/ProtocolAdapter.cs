using System.Text;
using System.Text.Json.Nodes;

namespace AiThread.Service.Proxy;

// The wire formats a coding agent can speak to the proxy. 9router translates each of them to whichever provider
// the chosen model lives on, so passthrough only has to keep the client's own format.
public enum ClientProtocol
{
    AnthropicMessages,
    OpenAiChat,
    OpenAiResponses,
    GeminiGenerate
}

// Rewrites a request of any supported protocol into the Anthropic Messages shape that ClaudeRequestInspector reads:
// { model, stream, system, tools: [{ name }], messages: [{ role, content }], metadata: { session_id } }.
// Only what the inspector needs survives (roles, text, tool call/result markers); the original body is what gets
// forwarded on passthrough.
public static class ProtocolAdapter
{
    public static JsonObject ToCanonical(ClientProtocol protocol, JsonObject body, string? pathModel = null, bool? pathStream = null) => protocol switch
    {
        ClientProtocol.OpenAiChat => FromOpenAiChat(body),
        ClientProtocol.OpenAiResponses => FromOpenAiResponses(body),
        ClientProtocol.GeminiGenerate => FromGemini(body, pathModel, pathStream),
        _ => body
    };

    // Path (with query, minus credentials) 9router serves the protocol on.
    public static string RouterPath(ClientProtocol protocol, string model, string? geminiAction, string query) => protocol switch
    {
        ClientProtocol.OpenAiChat => "/v1/chat/completions" + query,
        ClientProtocol.OpenAiResponses => "/v1/responses" + query,
        ClientProtocol.GeminiGenerate => $"/v1beta/models/{model}:{geminiAction ?? "generateContent"}" + query,
        _ => "/v1/messages" + query
    };

    private static JsonObject FromOpenAiChat(JsonObject body)
    {
        var system = new StringBuilder();
        var messages = new JsonArray();

        foreach (var message in body["messages"] as JsonArray ?? [])
        {
            var role = Str(message?["role"]) ?? "user";
            var content = message?["content"];
            switch (role)
            {
                case "system" or "developer":
                    system.AppendLine(PartsText(content));
                    break;
                case "tool":
                    messages.Add(Message("user", [ToolResult()]));
                    break;
                case "assistant":
                    var blocks = new JsonArray { Text(PartsText(content)) };
                    foreach (var call in message?["tool_calls"] as JsonArray ?? [])
                        blocks.Add(ToolUse(Str(call?["function"]?["name"])));
                    messages.Add(Message("assistant", blocks));
                    break;
                default:
                    messages.Add(Message("user", PartsContent(content)));
                    break;
            }
        }

        var tools = new JsonArray();
        foreach (var tool in body["tools"] as JsonArray ?? [])
            tools.Add(new JsonObject { ["name"] = Str(tool?["function"]?["name"]) ?? Str(tool?["name"]) ?? "tool" });

        return Canonical(Str(body["model"]), Flag(body["stream"]), system.ToString(), messages, tools, Str(body["prompt_cache_key"]));
    }

    // Codex talks this one: `instructions` plus an `input` list of typed items.
    private static JsonObject FromOpenAiResponses(JsonObject body)
    {
        var system = new StringBuilder(Str(body["instructions"]));
        var messages = new JsonArray();

        if (body["input"] is JsonValue plain && plain.TryGetValue<string>(out var text))
        {
            messages.Add(Message("user", [Text(text)]));
        }
        else
        {
            foreach (var item in body["input"] as JsonArray ?? [])
            {
                var type = Str(item?["type"]) ?? "message";
                switch (type)
                {
                    case "message":
                        var role = Str(item?["role"]) ?? "user";
                        if (role is "system" or "developer") system.AppendLine(PartsText(item?["content"]));
                        else messages.Add(Message(role == "assistant" ? "assistant" : "user", PartsContent(item?["content"])));
                        break;
                    case "function_call" or "custom_tool_call" or "local_shell_call":
                        messages.Add(Message("assistant", [ToolUse(Str(item?["name"]) ?? type)]));
                        break;
                    case "function_call_output" or "custom_tool_call_output" or "local_shell_call_output":
                        messages.Add(Message("user", [ToolResult()]));
                        break;
                }
            }
        }

        var tools = new JsonArray();
        foreach (var tool in body["tools"] as JsonArray ?? [])
            tools.Add(new JsonObject { ["name"] = Str(tool?["name"]) ?? Str(tool?["type"]) ?? "tool" });

        return Canonical(Str(body["model"]), Flag(body["stream"]), system.ToString(), messages, tools, Str(body["prompt_cache_key"]));
    }

    // Gemini native: the model and streaming flag live in the URL, not the body.
    private static JsonObject FromGemini(JsonObject body, string? pathModel, bool? pathStream)
    {
        var system = new StringBuilder();
        var instruction = body["systemInstruction"] ?? body["system_instruction"];
        foreach (var part in instruction?["parts"] as JsonArray ?? [])
            system.AppendLine(Str(part?["text"]));

        var messages = new JsonArray();
        foreach (var content in body["contents"] as JsonArray ?? [])
        {
            var blocks = new JsonArray();
            foreach (var part in content?["parts"] as JsonArray ?? [])
            {
                var call = part?["functionCall"] ?? part?["function_call"];
                var response = part?["functionResponse"] ?? part?["function_response"];
                var inline = part?["inlineData"] ?? part?["inline_data"];
                if (call is not null) blocks.Add(ToolUse(Str(call["name"])));
                else if (response is not null) blocks.Add(ToolResult());
                else if (inline is not null && Str(inline["data"]) is { } inlineData && Str(inline["mimeType"] ?? inline["mime_type"]) is { } mimeType)
                    blocks.Add(Image(mimeType, inlineData));
                else if (Str(part?["text"]) is { } partText) blocks.Add(Text(partText));
            }
            var role = Str(content?["role"]) == "model" ? "assistant" : "user";
            messages.Add(Message(role, blocks));
        }

        var tools = new JsonArray();
        foreach (var tool in body["tools"] as JsonArray ?? [])
        {
            foreach (var declaration in (tool?["functionDeclarations"] ?? tool?["function_declarations"]) as JsonArray ?? [])
                tools.Add(new JsonObject { ["name"] = Str(declaration?["name"]) ?? "tool" });
        }

        return Canonical(pathModel, pathStream ?? false, system.ToString(), messages, tools, null);
    }

    private static JsonObject Canonical(string? model, bool stream, string system, JsonArray messages, JsonArray tools, string? sessionId)
    {
        var canonical = new JsonObject
        {
            ["model"] = model ?? "unknown",
            ["stream"] = stream,
            ["system"] = system,
            ["messages"] = messages
        };
        if (tools.Count > 0) canonical["tools"] = tools;
        if (!string.IsNullOrWhiteSpace(sessionId)) canonical["metadata"] = new JsonObject { ["session_id"] = sessionId };
        return canonical;
    }

    private static JsonObject Message(string role, JsonArray content) => new() { ["role"] = role, ["content"] = content };

    private static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };

    private static JsonObject ToolUse(string? name) => new() { ["type"] = "tool_use", ["name"] = name ?? "tool" };

    private static JsonObject ToolResult() => new() { ["type"] = "tool_result" };

    // Canonical (Anthropic-shape) image block, same shape ClaudeRequestInspector reads regardless of which
    // client protocol the image arrived on.
    private static JsonObject Image(string mediaType, string data) => new()
    {
        ["type"] = "image",
        ["source"] = new JsonObject { ["type"] = "base64", ["media_type"] = mediaType, ["data"] = data }
    };

    // Chat/Responses content is either a string or a list of parts ({ type: text | input_text | output_text, text }).
    private static string PartsText(JsonNode? content)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var plain)) return plain;
        if (content is not JsonArray parts) return string.Empty;

        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            if (Str(part?["text"]) is { } text) builder.Append(text).Append('\n');
        }
        return builder.ToString();
    }

    // Same parts list as PartsText, but keeps attached images (data: URLs only — a remote image_url would need
    // the proxy to fetch it, which it does not do) as canonical image blocks instead of dropping them.
    private static JsonArray PartsContent(JsonNode? content)
    {
        var blocks = new JsonArray();
        if (content is JsonValue value && value.TryGetValue<string>(out var plain))
        {
            blocks.Add(Text(plain));
            return blocks;
        }

        if (content is JsonArray parts)
        {
            foreach (var part in parts)
            {
                if (Str(part?["text"]) is { } text)
                {
                    blocks.Add(Text(text));
                    continue;
                }

                // OpenAI chat: { type: "image_url", image_url: { url } }. OpenAI Responses: { type: "input_image", image_url: "<url>" }.
                var url = Str(part?["image_url"]?["url"]) ?? Str(part?["image_url"]);
                if (url is not null && TryParseDataUrl(url, out var mediaType, out var data))
                    blocks.Add(Image(mediaType, data));
            }
        }

        if (blocks.Count == 0) blocks.Add(Text(string.Empty));
        return blocks;
    }

    private static bool TryParseDataUrl(string url, out string mediaType, out string data)
    {
        mediaType = string.Empty;
        data = string.Empty;
        if (!url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;

        var comma = url.IndexOf(',');
        if (comma < 0) return false;

        var header = url[5..comma];
        if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) return false;

        mediaType = header[..^";base64".Length];
        data = url[(comma + 1)..];
        return mediaType.Length > 0 && data.Length > 0;
    }

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static bool Flag(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
}
