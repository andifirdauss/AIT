using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AiThread.Service.Proxy;

public sealed record ClaudeRequestInfo(
    bool IsOrchestratable,
    string Reason,
    bool Stream,
    string Model,
    string? WorkingDirectory,
    string? SolutionHint,
    string? SessionId,
    // Latest user turn without <system-reminder> blocks.
    string Prompt,
    // Latest user turn as sent (includes injected context such as CLAUDE.md).
    string PromptContext,
    string? ConversationContext,
    int EstimatedInputTokens);

// Reads an Anthropic Messages API request coming from Claude Code and decides whether it is a main-agent
// turn worth orchestrating or an auxiliary call (title generation, compaction, quota check...) to pass through.
public static partial class ClaudeRequestInspector
{
    public const string SolutionHeader = "X-AiThread-Solution";
    public const string WorkingDirectoryHeader = "X-AiThread-Cwd";

    private const int MaxHistoryMessages = 12;
    private const int MaxHistoryMessageChars = 1_500;
    private const int MaxHistoryChars = 12_000;

    public static ClaudeRequestInfo Inspect(JsonNode body, IReadOnlyDictionary<string, string?> headers, int rawLength)
    {
        var model = Str(body["model"]) ?? "unknown";
        var stream = body["stream"] is JsonValue streamValue && streamValue.TryGetValue<bool>(out var s) && s;
        var systemText = SystemText(body["system"]);
        var messages = body["messages"] as JsonArray ?? [];
        var tools = body["tools"] as JsonArray;

        var last = messages.Count > 0 ? messages[^1] : null;
        var (lastText, hasToolResult) = ContentText(last?["content"]);
        var prompt = StripReminders(lastText).Trim();

        var workingDirectory = Header(headers, WorkingDirectoryHeader)
                               ?? FindWorkingDirectory(systemText)
                               ?? messages.Select(m => FindWorkingDirectory(ContentText(m?["content"]).Text)).FirstOrDefault(d => d is not null);
        var sessionId = Header(headers, "x-claude-code-session-id") ?? SessionFromMetadata(body["metadata"]?["user_id"]);

        var (orchestratable, reason) =
            tools is null || tools.Count == 0 ? (false, "auxiliary request without tools")
            : !IsAgentSystemPrompt(systemText, workingDirectory) ? (false, "not a Claude Code agent request")
            : Str(last?["role"]) != "user" ? (false, "last message is not from the user")
            : hasToolResult ? (false, "tool_result turn")
            : prompt.Length == 0 ? (false, "empty prompt")
            : (true, "main agent turn");

        return new ClaudeRequestInfo(
            orchestratable,
            reason,
            stream,
            model,
            workingDirectory,
            Header(headers, SolutionHeader),
            sessionId,
            prompt,
            lastText.Trim(),
            orchestratable ? BuildHistory(messages) : null,
            Math.Max(1, rawLength / 4));
    }

    public static string StripReminders(string text) => ReminderBlock().Replace(text, string.Empty);

    private static bool IsAgentSystemPrompt(string systemText, string? workingDirectory) =>
        systemText.Contains("Claude Code", StringComparison.OrdinalIgnoreCase)
        || systemText.Contains("Claude Agent SDK", StringComparison.OrdinalIgnoreCase)
        || workingDirectory is not null;

    private static string? FindWorkingDirectory(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = WorkingDirectoryLine().Match(text);
        return match.Success ? match.Groups[1].Value.Trim().Trim('`', '"', '\'').TrimEnd('.', ' ') : null;
    }

    // Claude Code sends metadata.user_id as "user_<hash>_account_<uuid>_session_<uuid>" or as a JSON string.
    private static string? SessionFromMetadata(JsonNode? userId)
    {
        var value = Str(userId);
        if (string.IsNullOrEmpty(value)) return null;

        var match = SessionPattern().Match(value);
        if (match.Success) return match.Groups[1].Value;

        if (value.TrimStart().StartsWith('{'))
        {
            try
            {
                return Str(JsonNode.Parse(value)?["session_id"]);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        return null;
    }

    private static string? BuildHistory(JsonArray messages)
    {
        if (messages.Count <= 1) return null;

        var parts = new List<string>();
        var start = Math.Max(0, messages.Count - 1 - MaxHistoryMessages);
        for (var i = start; i < messages.Count - 1; i++)
        {
            var role = Str(messages[i]?["role"]) ?? "user";
            var (text, _) = ContentText(messages[i]?["content"], includeToolMarkers: true);
            text = StripReminders(text).Trim();
            if (text.Length == 0) continue;
            if (text.Length > MaxHistoryMessageChars) text = text[..MaxHistoryMessageChars] + " …";
            parts.Add($"[{role}] {text}");
        }

        // Keep the most recent part when the history is too long.
        var history = string.Join("\n\n", parts);
        return history.Length <= MaxHistoryChars ? history : "…" + history[^MaxHistoryChars..];
    }

    private static string SystemText(JsonNode? system) => system switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonArray blocks => string.Join("\n", blocks.Select(b => Str(b?["text"])).Where(t => t is not null)),
        _ => string.Empty
    };

    private static (string Text, bool HasToolResult) ContentText(JsonNode? content, bool includeToolMarkers = false)
    {
        if (content is JsonValue value && value.TryGetValue<string>(out var plain)) return (plain, false);
        if (content is not JsonArray blocks) return (string.Empty, false);

        var builder = new StringBuilder();
        var hasToolResult = false;
        foreach (var block in blocks)
        {
            switch (Str(block?["type"]))
            {
                case "text":
                    if (Str(block?["text"]) is { } text) builder.Append(text).Append('\n');
                    break;
                case "tool_result":
                    hasToolResult = true;
                    if (includeToolMarkers) builder.Append("[tool result]\n");
                    break;
                case "tool_use":
                    if (includeToolMarkers) builder.Append($"[tool call: {Str(block?["name"])}]\n");
                    break;
            }
        }
        return (builder.ToString(), hasToolResult);
    }

    private static string? Header(IReadOnlyDictionary<string, string?> headers, string name) =>
        headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    [GeneratedRegex(@"<system-reminder>[\s\S]*?</system-reminder>", RegexOptions.IgnoreCase)]
    private static partial Regex ReminderBlock();

    [GeneratedRegex(@"(?:Primary working directory|Working directory)\s*:\s*([^\r\n]+)", RegexOptions.IgnoreCase)]
    private static partial Regex WorkingDirectoryLine();

    [GeneratedRegex(@"session_([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex SessionPattern();
}
