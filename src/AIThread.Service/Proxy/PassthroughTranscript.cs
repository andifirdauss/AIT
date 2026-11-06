using System.Text;
using System.Text.Json.Nodes;
using AIThread.Service.Models;

namespace AIThread.Service.Proxy;

// A client request (any ClientProtocol) as plain OpenAI-shaped chat messages, for a passthrough answered by a model
// without a 9router route (an official CLI, CliBackend). The reply is text only: tool calls and results already in the
// conversation are kept as text, and the client's tools are not offered, so the model is told so.
public static class PassthroughTranscript
{
    public const string TextOnlyNote =
        "Note from AIT: no tools are available for this reply. Answer in plain text only; do not write tool calls or function-call markup.";

    // Messages for ChatCompletionRequest; hadTools says the client offered tools that this reply cannot use.
    public static (JsonArray Messages, bool HadTools) ToMessages(ClientProtocol protocol, JsonObject body)
    {
        var turns = new List<(string Role, StringBuilder Text, List<ImageAttachment> Images)>();
        var system = new StringBuilder();
        var hadTools = (body["tools"] as JsonArray ?? []).Count > 0;

        switch (protocol)
        {
            case ClientProtocol.OpenAiChat:
                FromOpenAiChat(body, system, turns);
                break;
            case ClientProtocol.OpenAiResponses:
                FromOpenAiResponses(body, system, turns);
                break;
            case ClientProtocol.GeminiGenerate:
                FromGemini(body, system, turns);
                break;
            default:
                FromAnthropic(body, system, turns);
                break;
        }

        if (hadTools) Append(system, TextOnlyNote);
        var messages = new JsonArray();
        if (system.Length > 0) messages.Add(ChatMessages.System(system.ToString().Trim()));
        foreach (var (role, text, images) in turns)
        {
            var content = text.ToString().Trim();
            if (content.Length == 0 && images.Count == 0) continue;
            messages.Add(role == "assistant" ? ChatMessages.Assistant(content) : ChatMessages.User(content, images));
        }
        return (messages, hadTools);
    }

    private static void FromAnthropic(JsonObject body, StringBuilder system, List<(string, StringBuilder, List<ImageAttachment>)> turns)
    {
        Append(system, TextOf(body["system"]));
        foreach (var message in (body["messages"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var turn = Turn(turns, Str(message["role"]) == "assistant" ? "assistant" : "user");
            var content = message["content"];
            if (Str(content) is { } plain)
            {
                Append(turn.Text, plain);
                continue;
            }
            foreach (var block in (content as JsonArray ?? []).OfType<JsonObject>())
            {
                switch (Str(block["type"]))
                {
                    case "text":
                        Append(turn.Text, Str(block["text"]));
                        break;
                    case "image" when block["source"] is JsonObject source && Str(source["type"]) == "base64":
                        if (Str(source["media_type"]) is { } mediaType && Str(source["data"]) is { } data)
                            turn.Images.Add(new ImageAttachment(mediaType, data));
                        break;
                    case "tool_use":
                        Append(turn.Text, ToolCallText(Str(block["name"]), block["input"]?.ToJsonString()));
                        break;
                    case "tool_result":
                        Append(turn.Text, ToolResultText(TextOf(block["content"])));
                        break;
                }
            }
        }
    }

    private static void FromOpenAiChat(JsonObject body, StringBuilder system, List<(string, StringBuilder, List<ImageAttachment>)> turns)
    {
        foreach (var message in (body["messages"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var role = Str(message["role"]) ?? "user";
            if (role is "system" or "developer")
            {
                Append(system, TextOf(message["content"]));
                continue;
            }
            var turn = Turn(turns, role == "assistant" ? "assistant" : "user");
            if (role == "tool")
            {
                Append(turn.Text, ToolResultText(TextOf(message["content"])));
                continue;
            }
            Append(turn.Text, TextOf(message["content"]));
            AddImages(message["content"], turn.Images);
            foreach (var call in (message["tool_calls"] as JsonArray ?? []).OfType<JsonObject>())
                Append(turn.Text, ToolCallText(Str(call["function"]?["name"]), Str(call["function"]?["arguments"])));
        }
    }

    private static void FromOpenAiResponses(JsonObject body, StringBuilder system, List<(string, StringBuilder, List<ImageAttachment>)> turns)
    {
        Append(system, Str(body["instructions"]));
        if (Str(body["input"]) is { } plain)
        {
            Append(Turn(turns, "user").Text, plain);
            return;
        }
        foreach (var item in (body["input"] as JsonArray ?? []).OfType<JsonObject>())
        {
            switch (Str(item["type"]) ?? "message")
            {
                case "message":
                    var role = Str(item["role"]) ?? "user";
                    if (role is "system" or "developer")
                    {
                        Append(system, TextOf(item["content"]));
                        break;
                    }
                    var turn = Turn(turns, role == "assistant" ? "assistant" : "user");
                    Append(turn.Text, TextOf(item["content"]));
                    AddImages(item["content"], turn.Images);
                    break;
                case "function_call" or "custom_tool_call" or "local_shell_call":
                    Append(Turn(turns, "assistant").Text, ToolCallText(Str(item["name"]), Str(item["arguments"]) ?? Str(item["input"]) ?? item["action"]?.ToJsonString()));
                    break;
                case "function_call_output" or "custom_tool_call_output" or "local_shell_call_output":
                    Append(Turn(turns, "user").Text, ToolResultText(TextOf(item["output"])));
                    break;
            }
        }
    }

    private static void FromGemini(JsonObject body, StringBuilder system, List<(string, StringBuilder, List<ImageAttachment>)> turns)
    {
        foreach (var part in ((body["systemInstruction"] ?? body["system_instruction"])?["parts"] as JsonArray ?? []).OfType<JsonObject>())
            Append(system, Str(part["text"]));

        foreach (var content in (body["contents"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var turn = Turn(turns, Str(content["role"]) == "model" ? "assistant" : "user");
            foreach (var part in (content["parts"] as JsonArray ?? []).OfType<JsonObject>())
            {
                if ((part["functionCall"] ?? part["function_call"]) is JsonObject call)
                    Append(turn.Text, ToolCallText(Str(call["name"]), call["args"]?.ToJsonString()));
                else if ((part["functionResponse"] ?? part["function_response"]) is JsonObject response)
                    Append(turn.Text, ToolResultText(response["response"]?.ToJsonString() ?? string.Empty));
                else if ((part["inlineData"] ?? part["inline_data"]) is JsonObject inline
                         && Str(inline["mimeType"] ?? inline["mime_type"]) is { } mimeType && Str(inline["data"]) is { } data)
                    turn.Images.Add(new ImageAttachment(mimeType, data));
                else
                    Append(turn.Text, Str(part["text"]));
            }
        }
    }

    // Consecutive messages of one role become one turn (a tool result after a user text is still the user's turn).
    private static (string Role, StringBuilder Text, List<ImageAttachment> Images) Turn(
        List<(string Role, StringBuilder Text, List<ImageAttachment> Images)> turns, string role)
    {
        if (turns.Count > 0 && turns[^1].Role == role) return turns[^1];
        var turn = (role, new StringBuilder(), new List<ImageAttachment>());
        turns.Add(turn);
        return turn;
    }

    private static string ToolCallText(string? name, string? arguments) => $"[Called tool {name ?? "tool"} with {arguments ?? "{}"}]";

    private static string ToolResultText(string result) => $"[Tool result]\n{result.Trim()}";

    // A string, or a list of parts / blocks of which the text ones count.
    private static string TextOf(JsonNode? content)
    {
        if (Str(content) is { } plain) return plain;
        if (content is not JsonArray parts) return content is null ? string.Empty : content.ToJsonString();
        var text = new StringBuilder();
        foreach (var part in parts.OfType<JsonObject>())
            if (Str(part["text"]) is { } partText) Append(text, partText);
        return text.ToString();
    }

    // OpenAI chat { type: image_url, image_url: { url } } and Responses { type: input_image, image_url: "<url>" }, data URLs only.
    private static void AddImages(JsonNode? content, List<ImageAttachment> images)
    {
        foreach (var part in (content as JsonArray ?? []).OfType<JsonObject>())
        {
            var url = Str(part["image_url"]?["url"]) ?? Str(part["image_url"]);
            if (url is null || !url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var comma = url.IndexOf(',');
            if (comma < 0) continue;
            var header = url[5..comma];
            if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase)) continue;
            images.Add(new ImageAttachment(header[..^";base64".Length], url[(comma + 1)..]));
        }
    }

    private static void Append(StringBuilder text, string? part)
    {
        if (string.IsNullOrWhiteSpace(part)) return;
        if (text.Length > 0) text.AppendLine().AppendLine();
        text.Append(part.Trim());
    }

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
