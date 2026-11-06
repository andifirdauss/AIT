using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.CliBackend;

// Turns an OpenAI-shaped chat request (what every AIT agent builds) into one prompt for a CLI that has no function
// calling of its own. Tools become a written protocol: the model answers with <tool_call> blocks (CliToolCallParser
// reads them back), and earlier calls and results are replayed in the same tags so the model sees one consistent format.
// The CLI runs stateless, so the whole conversation goes into every call.
public static class CliTranscript
{
    public const string ToolCallTag = "tool_call";
    public const string ToolResultTag = "tool_result";

    public static CliPrompt Render(JsonArray messages, JsonArray? tools)
    {
        var system = new StringBuilder();
        var turns = new List<JsonObject>();
        foreach (var node in messages)
        {
            if (node is not JsonObject message) continue;
            if (Role(message) == "system") Append(system, TextOf(message["content"]));
            else turns.Add(message);
        }

        var toolList = tools is { Count: > 0 } ? tools : null;
        if (toolList is not null) Append(system, ToolProtocol(toolList));

        var images = new List<CliImage>();
        var last = turns.Count > 0 ? turns[^1] : null;
        if (last is not null && Role(last) == "user") images.AddRange(ImagesOf(last["content"]));

        // A single user turn goes as it is: the text-only agents (executor, wrapper, reviewer, memory) read exactly
        // what they would have read through 9router.
        if (turns.Count == 1 && Role(turns[0]) == "user")
            return new CliPrompt(system.ToString().Trim(), TextOf(turns[0]["content"]).Trim(), images);

        return new CliPrompt(system.ToString().Trim(), Conversation(turns, toolList is not null), images);
    }

    private static string Conversation(IReadOnlyList<JsonObject> turns, bool hasTools)
    {
        var callNames = new Dictionary<string, string>(StringComparer.Ordinal);
        var text = new StringBuilder();
        text.AppendLine("The conversation so far, oldest first:");
        text.AppendLine();
        text.AppendLine("<conversation>");
        foreach (var turn in turns)
        {
            switch (Role(turn))
            {
                case "assistant":
                    text.AppendLine("<assistant>");
                    var content = TextOf(turn["content"]).Trim();
                    if (content.Length > 0) text.AppendLine(content);
                    if (turn["tool_calls"] is JsonArray calls)
                    {
                        foreach (var call in calls.OfType<JsonObject>())
                        {
                            var id = call["id"]?.GetValue<string>() ?? string.Empty;
                            var name = call["function"]?["name"]?.GetValue<string>() ?? string.Empty;
                            var arguments = call["function"]?["arguments"]?.GetValue<string>() ?? "{}";
                            if (id.Length > 0) callNames[id] = name;
                            text.Append('<').Append(ToolCallTag).Append(" name=\"").Append(name).Append("\">")
                                .Append(arguments.Trim()).Append("</").Append(ToolCallTag).AppendLine(">");
                        }
                    }
                    text.AppendLine("</assistant>");
                    break;
                case "tool":
                    var callId = turn["tool_call_id"]?.GetValue<string>() ?? string.Empty;
                    var tool = callNames.GetValueOrDefault(callId, "tool");
                    text.Append('<').Append(ToolResultTag).Append(" name=\"").Append(tool).AppendLine("\">");
                    text.AppendLine(TextOf(turn["content"]).Trim());
                    text.Append("</").Append(ToolResultTag).AppendLine(">");
                    break;
                default:
                    text.AppendLine("<user>");
                    text.AppendLine(TextOf(turn["content"]).Trim());
                    text.AppendLine("</user>");
                    break;
            }
        }
        text.AppendLine("</conversation>");
        text.AppendLine();
        text.Append(hasTools
            ? "Write your next assistant reply now: either <tool_call> blocks, or your final text answer. Do not repeat the conversation."
            : "Write your next assistant reply now. Do not repeat the conversation.");
        return text.ToString();
    }

    private static string ToolProtocol(JsonArray tools)
    {
        var text = new StringBuilder();
        text.AppendLine("# Calling tools");
        text.AppendLine("You have no built-in tools. AIT runs the tools listed below for you. To call one or more of them, reply with one block per call in exactly this form, and write nothing after the last block:");
        text.AppendLine();
        text.Append('<').Append(ToolCallTag).Append(" name=\"TOOL_NAME\">{\"argument\": \"value\"}</").Append(ToolCallTag).AppendLine(">");
        text.AppendLine();
        text.AppendLine("The content of a block is one JSON object with the tool's parameters (no code fence around it). AIT answers with <tool_result> blocks in the next turn. To answer without calling a tool, reply with plain text and no <tool_call> block. Never write a <tool_result> yourself.");
        text.AppendLine();
        text.AppendLine("## Tools");
        foreach (var tool in tools.OfType<JsonObject>())
        {
            var function = tool["function"] as JsonObject ?? tool;
            var name = function["name"]?.GetValue<string>();
            if (string.IsNullOrEmpty(name)) continue;
            text.AppendLine();
            text.Append("### ").AppendLine(name);
            var description = function["description"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(description)) text.AppendLine(description.Trim());
            if (function["parameters"] is JsonNode parameters)
                text.Append("Parameters (JSON Schema): ").AppendLine(parameters.ToJsonString(Compact));
        }
        return text.ToString();
    }

    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    private static string? Role(JsonObject message) => message["role"]?.GetValue<string>();

    // Content is a string, or (OpenAI vision) an array of parts of which only the text parts are text.
    private static string TextOf(JsonNode? content) => content switch
    {
        null => string.Empty,
        JsonValue value when value.TryGetValue<string>(out var s) => s,
        JsonArray parts => string.Join("\n", parts.OfType<JsonObject>()
            .Where(p => p["type"]?.GetValue<string>() == "text")
            .Select(p => p["text"]?.GetValue<string>() ?? string.Empty)),
        _ => content.ToJsonString()
    };

    // data:<mime>;base64,<data> parts of ChatMessages.User(content, images).
    private static IEnumerable<CliImage> ImagesOf(JsonNode? content)
    {
        if (content is not JsonArray parts) yield break;
        foreach (var part in parts.OfType<JsonObject>())
        {
            if (part["type"]?.GetValue<string>() != "image_url") continue;
            var url = part["image_url"]?["url"]?.GetValue<string>();
            if (url is null || !url.StartsWith("data:", StringComparison.Ordinal)) continue;
            var comma = url.IndexOf(',');
            var header = comma > 5 ? url[5..comma] : string.Empty;
            if (comma < 0 || !header.EndsWith(";base64", StringComparison.Ordinal)) continue;
            yield return new CliImage(header[..^";base64".Length], url[(comma + 1)..]);
        }
    }

    private static void Append(StringBuilder text, string part)
    {
        if (string.IsNullOrWhiteSpace(part)) return;
        if (text.Length > 0) text.AppendLine().AppendLine();
        text.Append(part.Trim());
    }
}
