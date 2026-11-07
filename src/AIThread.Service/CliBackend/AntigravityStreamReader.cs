using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.CliBackend;

// Reads the NDJSON events of `agy --output-format stream-json`, one line at a time: "step_update" events whose
// step_type is "agent_response" carry text_delta chunks; "tool" steps are tools the model tried; the final "result"
// holds status ("SUCCESS" / "ERROR"), the whole response, an error and the usage.
public sealed class AntigravityStreamReader
{
    private readonly StringBuilder _deltas = new();
    private readonly HashSet<int> _toolSteps = [];

    // The conversation agy saved for this run (init and result events carry it).
    public string? ConversationId { get; private set; }
    public bool SawResult { get; private set; }
    public bool IsError { get; private set; }
    public string? ResultText { get; private set; }
    public string? Error { get; private set; }
    public string? FinishedReason { get; private set; }
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public int CacheReadTokens { get; private set; }
    public int ToolUseCount => _toolSteps.Count;

    // The reply: the result's response when there is one, else what was streamed.
    public string Text => !string.IsNullOrEmpty(ResultText) ? ResultText : _deltas.ToString();

    // Returns the text chunk carried by the line, if any. Lines that are not JSON are ignored.
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
        if (Str(json["conversation_id"]) is { Length: > 0 } conversation) ConversationId ??= conversation;

        switch (Str(json["event"]))
        {
            case "step_update" when json["step_update"] is JsonObject step:
                switch (Str(step["step_type"]))
                {
                    case "agent_response" when Str(step["text_delta"]) is { Length: > 0 } delta:
                        _deltas.Append(delta);
                        return delta;
                    case "tool":
                        if (step["step_index"] is JsonValue index && index.TryGetValue<int>(out var i)) _toolSteps.Add(i);
                        return null;
                    default:
                        return null;
                }
            case "result" when json["result"] is JsonObject result:
                SawResult = true;
                if (Str(result["conversation_id"]) is { Length: > 0 } resultConversation) ConversationId ??= resultConversation;
                IsError = !string.Equals(Str(result["status"]), "SUCCESS", StringComparison.OrdinalIgnoreCase);
                ResultText = Str(result["response"]);
                Error = Str(result["error"]) ?? (result["error"] is JsonObject error ? Str(error["message"]) : null);
                FinishedReason = Str(result["finished_reason"]);
                if (result["usage"] is JsonObject usage)
                {
                    InputTokens = Int(usage["input_tokens"]);
                    OutputTokens = Int(usage["output_tokens"]);
                    CacheReadTokens = Int(usage["cache_read_tokens"]);
                }
                return null;
            default:
                return null;
        }
    }

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var n) ? (int)Math.Min(n, int.MaxValue) : 0;
}
