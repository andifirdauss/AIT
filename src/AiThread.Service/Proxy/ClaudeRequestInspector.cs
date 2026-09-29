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

        // Find the active user turn. Claude Code may append system reminders (role: "system")
        // and/or an assistant prefill (role: "assistant") after the user's prompt.
        var userIndex = -1;
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Str(messages[i]?["role"]), "user", StringComparison.OrdinalIgnoreCase))
            {
                userIndex = i;
                break;
            }
        }

        var userMessage = userIndex >= 0 ? messages[userIndex] : null;
        var (userText, hasToolResult) = ContentText(userMessage?["content"]);
        var prompt = StripReminders(userText).Trim();

        var promptContextBuilder = new StringBuilder(userText.Trim());
        for (var i = userIndex + 1; i < messages.Count; i++)
        {
            if (string.Equals(Str(messages[i]?["role"]), "system", StringComparison.OrdinalIgnoreCase))
            {
                var (sysText, _) = ContentText(messages[i]?["content"]);
                if (!string.IsNullOrWhiteSpace(sysText))
                {
                    promptContextBuilder.Append("\n\n").Append(sysText.Trim());
                }
            }
        }
        var promptContext = promptContextBuilder.ToString();

        var workingDirectory = Header(headers, WorkingDirectoryHeader)
                               ?? FindWorkingDirectory(systemText)
                               ?? (userMessage is not null ? FindWorkingDirectory(userText) : null)
                               ?? messages.Reverse().Select(m => FindWorkingDirectory(ContentText(m?["content"]).Text)).FirstOrDefault(d => d is not null);
        var sessionId = Header(headers, "x-claude-code-session-id") ?? SessionFromMetadata(body["metadata"]?["user_id"]);

        var lastRole = messages.Count > 0 ? Str(messages[^1]?["role"]) : "none";
        var (orchestratable, reason) =
            tools is null || tools.Count == 0 ? (false, "auxiliary request without tools")
            : !IsAgentSystemPrompt(systemText, workingDirectory) ? (false, "not a Claude Code agent request")
            : userMessage is null ? (false, $"last message is not from the user (role: {lastRole})")
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
            promptContext,
            orchestratable ? BuildHistory(messages, userIndex) : null,
            Math.Max(1, rawLength / 4));
    }

    public static string StripReminders(string text) => ReminderBlock().Replace(text, string.Empty);

    private static bool IsAgentSystemPrompt(string systemText, string? workingDirectory) =>
        string.IsNullOrEmpty(systemText)
        || systemText.Contains("Claude", StringComparison.OrdinalIgnoreCase)
        || systemText.Contains("Anthropic", StringComparison.OrdinalIgnoreCase)
        || workingDirectory is not null;

    private static string? FindWorkingDirectory(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = WorkingDirectoryLine().Match(text);
        if (match.Success)
        {
            var raw = match.Groups[1].Value.Trim().Trim('`', '"', '\'').TrimEnd('.', ' ');
            var wasIndex = raw.IndexOf("(was", StringComparison.OrdinalIgnoreCase);
            if (wasIndex >= 0) raw = raw[..wasIndex].Trim().Trim('`', '"', '\'').TrimEnd('.', ' ');
            if (raw.Length > 0) return raw;
        }

        var claudeMdMatch = ClaudeMdLine().Match(text);
        if (claudeMdMatch.Success)
        {
            var raw = claudeMdMatch.Groups[1].Value.Trim().Trim('`', '"', '\'').TrimEnd('.', ' ');
            if (raw.Length > 0) return raw;
        }

        return null;
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

    private static string? BuildHistory(JsonArray messages, int userIndex)
    {
        var end = userIndex >= 0 ? userIndex : messages.Count - 1;
        if (end <= 0) return null;

        var parts = new List<string>();
        var start = Math.Max(0, end - MaxHistoryMessages);
        for (var i = start; i < end; i++)
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

    [GeneratedRegex(@"Contents of\s+([^\r\n]+?)[\\/]CLAUDE\.md", RegexOptions.IgnoreCase)]
    private static partial Regex ClaudeMdLine();

    [GeneratedRegex(@"session_([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex SessionPattern();
}
