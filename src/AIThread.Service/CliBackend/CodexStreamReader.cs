using System.Text.Json;
using System.Text.Json.Nodes;

namespace AIThread.Service.CliBackend;

// Reads the JSONL events of `codex exec --json`, one line at a time: agent messages (item.* with item.type
// "agent_message"), commands the model tried to run, the usage of turn.completed, and the failure of turn.failed or a
// fatal "error" event. Codex sends a message whole (item.completed), not as text deltas.
public sealed class CodexStreamReader
{
    private readonly List<string> _errors = [];

    public bool Completed { get; private set; }
    public string? FailedMessage { get; private set; }
    public int CommandCount { get; private set; }
    public int InputTokens { get; private set; }
    public int CachedInputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public IReadOnlyList<string> Errors => _errors;

    // The reply: the last agent message (a preamble before a step the model tried is not the answer).
    public string Text { get; private set; } = string.Empty;

    // An HTTP status in the failure text ("unexpected status 401 Unauthorized: …"), for the retry/fallback mapping.
    public int? ApiStatus => CliFailure.StatusIn(FailedMessage) ?? _errors.Select(CliFailure.StatusIn).FirstOrDefault(s => s is not null);

    // Lines that are not JSON (a warning printed by the CLI) are ignored.
    public void Feed(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        JsonObject? json;
        try
        {
            json = JsonNode.Parse(line) as JsonObject;
        }
        catch (JsonException)
        {
            return;
        }
        if (json is null) return;

        switch (Str(json["type"]))
        {
            case "item.completed" when json["item"] is JsonObject item:
                switch (Str(item["type"]))
                {
                    case "agent_message" when Str(item["text"]) is { Length: > 0 } text:
                        Text = text;
                        break;
                    case "command_execution":
                        CommandCount++;
                        break;
                    case "error" when Str(item["message"]) is { Length: > 0 } message:
                        _errors.Add(message);
                        break;
                }
                break;
            case "turn.completed":
                Completed = true;
                if (json["usage"] is JsonObject usage)
                {
                    InputTokens = Int(usage["input_tokens"]);
                    CachedInputTokens = Int(usage["cached_input_tokens"]);
                    OutputTokens = Int(usage["output_tokens"]);
                }
                break;
            case "turn.failed":
                FailedMessage = Str(json["error"]?["message"]) ?? FailedMessage ?? "turn failed";
                break;
            case "error" when Str(json["message"]) is { Length: > 0 } error:
                _errors.Add(error);
                break;
        }
    }

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    private static int Int(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var n) ? (int)Math.Min(n, int.MaxValue) : 0;
}
