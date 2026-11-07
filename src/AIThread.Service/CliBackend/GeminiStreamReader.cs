using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.CliBackend;

// Reads the JSONL events of `gemini -p --output-format stream-json`, one line at a time: assistant message chunks
// ("message", role "assistant", delta), tools the model tried, non-fatal "error" events, and the final "result" with
// its status, error and token stats.
public sealed class GeminiStreamReader
{
    private readonly StringBuilder _text = new();
    private readonly List<string> _errors = [];

    public bool SawResult { get; private set; }
    public bool IsError { get; private set; }
    public string? ErrorType { get; private set; }
    public string? ErrorMessage { get; private set; }
    public int ToolUseCount { get; private set; }
    public int InputTokens { get; private set; }
    public int CachedTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public IReadOnlyList<string> Errors => _errors;

    public string Text => _text.ToString();

    // Returns the text chunk carried by the line, if any. Lines that are not JSON (a warning printed by the CLI) are
    // ignored.
    public string? Feed(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        JsonObject? json;
        try
        {
            json = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
        if (json is null) return null;

        switch (Str(json["type"]))
        {
            case "message" when Str(json["role"]) == "assistant" && Str(json["content"]) is { Length: > 0 } content:
                // A message without delta is the whole message: it replaces what was streamed of it.
                if (json["delta"] is JsonValue d && d.TryGetValue<bool>(out var isDelta) && isDelta)
                {
                    _text.Append(content);
                    return content;
                }
                _text.Clear().Append(content);
                return null;
            case "tool_use":
                ToolUseCount++;
                return null;
            case "error" when Str(json["message"]) is { Length: > 0 } message:
                _errors.Add(message);
                return null;
            case "result":
                SawResult = true;
                IsError = Str(json["status"]) is { } status && status != "success";
                if (json["error"] is JsonObject error)
                {
                    IsError = true;
                    ErrorType = Str(error["type"]);
                    ErrorMessage = Str(error["message"]);
                }
                if (json["stats"] is JsonObject stats)
                {
                    InputTokens = Int(stats["input_tokens"]);
                    CachedTokens = Int(stats["cached"]);
                    OutputTokens = Int(stats["output_tokens"]);
                }
                return null;
            default:
                return null;
        }
    }

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var n) ? (int)Math.Min(n, int.MaxValue) : 0;
}
