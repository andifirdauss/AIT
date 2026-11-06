using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.CliBackend;

// Reads the stream-json output of `claude -p --output-format stream-json --verbose --include-partial-messages`, one
// line at a time: text deltas as they arrive (stream_event / content_block_delta), the full assistant message, and the
// final result line with the usage and the error state.
public sealed class ClaudeStreamReader
{
    private readonly StringBuilder _deltas = new();
    private readonly StringBuilder _assistant = new();

    public bool SawResult { get; private set; }
    public bool IsError { get; private set; }
    public string? ResultText { get; private set; }
    public string? ResultSubtype { get; private set; }
    public int? ApiErrorStatus { get; private set; }
    public string? StopReason { get; private set; }
    public IReadOnlyList<string> Errors { get; private set; } = [];
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public int CacheReadTokens { get; private set; }
    public int CacheCreationTokens { get; private set; }

    // The reply: the result text when the CLI gave one, else what was streamed, else the assistant message.
    public string Text => !string.IsNullOrEmpty(ResultText) && !IsError
        ? ResultText
        : _deltas.Length > 0 ? _deltas.ToString() : _assistant.ToString();

    // Anthropic's stop reasons in OpenAI's words, which the agents check ("length" = cut at max tokens).
    public string? FinishReason => StopReason switch
    {
        null => null,
        "max_tokens" => "length",
        "end_turn" or "stop_sequence" => "stop",
        _ => StopReason
    };

    // Returns the text delta carried by the line, if any. Lines that are not JSON (a warning printed by the CLI) are
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
            case "stream_event":
                return StreamEvent(json["event"] as JsonObject);
            case "assistant":
                if (json["message"] is JsonObject message)
                {
                    foreach (var part in (message["content"] as JsonArray ?? []).OfType<JsonObject>())
                        if (Str(part["type"]) == "text") _assistant.Append(Str(part["text"]));
                    if (Str(message["stop_reason"]) is { } stop) StopReason = stop;
                }
                return null;
            case "result":
                SawResult = true;
                ResultSubtype = Str(json["subtype"]);
                IsError = json["is_error"] is JsonValue e && e.TryGetValue<bool>(out var isError) && isError
                          || ResultSubtype is not null && ResultSubtype != "success";
                ResultText = Str(json["result"]);
                if (Str(json["stop_reason"]) is { } reason) StopReason = reason;
                if (json["api_error_status"] is JsonValue s && s.TryGetValue<int>(out var status)) ApiErrorStatus = status;
                Errors = (json["errors"] as JsonArray ?? []).Select(Str).OfType<string>().ToList();
                if (json["usage"] is JsonObject usage) Usage(usage);
                return null;
            default:
                return null;
        }
    }

    private string? StreamEvent(JsonObject? streamEvent)
    {
        if (streamEvent is null) return null;
        switch (Str(streamEvent["type"]))
        {
            case "content_block_delta" when streamEvent["delta"] is JsonObject delta && Str(delta["type"]) == "text_delta":
                var text = Str(delta["text"]);
                if (string.IsNullOrEmpty(text)) return null;
                _deltas.Append(text);
                return text;
            case "message_delta" when streamEvent["delta"] is JsonObject messageDelta:
                if (Str(messageDelta["stop_reason"]) is { } stop) StopReason = stop;
                return null;
            default:
                return null;
        }
    }

    private void Usage(JsonObject usage)
    {
        InputTokens = Int(usage["input_tokens"]);
        OutputTokens = Int(usage["output_tokens"]);
        CacheReadTokens = Int(usage["cache_read_input_tokens"]);
        CacheCreationTokens = Int(usage["cache_creation_input_tokens"]);
    }

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var n) ? (int)Math.Min(n, int.MaxValue) : 0;
}
