using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIThread.Service.Models;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Proxy;

public sealed record ClaudeRequestInfo(
    bool IsOrchestratable,
    string Reason,
    bool Stream,
    string Model,
    string? WorkingDirectory,
    string? ProjectHint,
    string? SessionId,
    // Latest user turn without <system-reminder> and <system-notice> blocks.
    string Prompt,
    // Latest user turn as sent (includes injected context such as CLAUDE.md).
    string PromptContext,
    string? ConversationContext,
    int EstimatedInputTokens,
    // AIThread threads whose reply is still in the conversation, oldest first.
    IReadOnlyList<ThreadAnchorRef> Anchors,
    // Images attached to the active user turn (Claude Code/Codex/Gemini CLI vision input).
    IReadOnlyList<ImageAttachment> Images);

public sealed record ThreadAnchorRef(string ShortId, int MessageIndex);

// Reads an Anthropic Messages API request coming from Claude Code and decides whether it is a main-agent
// turn worth orchestrating or an auxiliary call (title generation, compaction, quota check...) to pass through.
public static partial class ClaudeRequestInspector
{
    // Pins the session to a project root folder. The old name carried a .sln path and is still accepted.
    public const string ProjectHeader = "X-AIThread-Project";
    public const string LegacySolutionHeader = "X-AIThread-Solution";
    public const string WorkingDirectoryHeader = "X-AIThread-Cwd";
    public const string SessionHeader = "X-AIThread-Session";

    // Session id headers in priority order: explicit AIThread header, Claude Code, Codex, generic agents.
    public static readonly string[] SessionHeaders = [SessionHeader, "x-claude-code-session-id", "session_id", "x-session-id"];

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

        var userIndex = LastUserIndex(messages);
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
        var sessionId = SessionHeaders.Select(name => Header(headers, name)).FirstOrDefault(v => v is not null)
                        ?? Str(body["metadata"]?["session_id"])
                        ?? SessionFromMetadata(body["metadata"]?["user_id"]);

        var lastRole = messages.Count > 0 ? Str(messages[^1]?["role"]) : "none";
        var (orchestratable, reason) =
            tools is null || tools.Count == 0 ? (false, "auxiliary request without tools")
            : userMessage is null ? (false, $"last message is not from the user (role: {lastRole})")
            : hasToolResult ? (false, "tool_result turn")
            : prompt.Length == 0 ? (false, "empty prompt")
            : SideRequest().IsMatch(prompt) ? (false, "Claude Code side request (session title / recap)")
            : (true, "main agent turn");

        return new ClaudeRequestInfo(
            orchestratable,
            reason,
            stream,
            model,
            workingDirectory,
            Header(headers, ProjectHeader) ?? Header(headers, LegacySolutionHeader),
            sessionId,
            prompt,
            promptContext,
            orchestratable ? BuildHistory(messages, 0, userIndex) : null,
            Math.Max(1, rawLength / 4),
            orchestratable ? FindAnchors(messages, userIndex) : [],
            orchestratable ? ExtractImages(userMessage?["content"]) : []);
    }

    // The conversation between the message at afterIndex and the active user turn, for when the context keeper's
    // summary already covers everything up to that message.
    public static string? HistoryAfter(JsonNode body, int afterIndex)
    {
        var messages = body["messages"] as JsonArray ?? [];
        return BuildHistory(messages, afterIndex + 1, LastUserIndex(messages));
    }

    public static string StripReminders(string text) => ReminderBlock().Replace(text, string.Empty);

    private static string? FindWorkingDirectory(string text)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var match = WorkingDirectoryLine().Match(text);
        if (!match.Success) match = WorkingDirectoryTag().Match(text);
        if (match.Success)
        {
            var raw = match.Groups["path"].Value.Trim().Trim('`', '"', '\'').TrimEnd('.', ' ');
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

    // Find the active user turn. Claude Code may append system reminders (role: "system")
    // and/or an assistant prefill (role: "assistant") after the user's prompt.
    private static int LastUserIndex(JsonArray messages)
    {
        for (var i = messages.Count - 1; i >= 0; i--)
        {
            if (string.Equals(Str(messages[i]?["role"]), "user", StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static List<ThreadAnchorRef> FindAnchors(JsonArray messages, int userIndex)
    {
        var anchors = new List<ThreadAnchorRef>();
        for (var i = 0; i < userIndex; i++)
        {
            if (!string.Equals(Str(messages[i]?["role"]), "assistant", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var id in ThreadAnchor.Find(ContentText(messages[i]?["content"]).Text))
                anchors.Add(new ThreadAnchorRef(id, i));
        }
        return anchors;
    }

    // The last MaxHistoryMessages messages from index `from` up to (not including) the active user turn.
    private static string? BuildHistory(JsonArray messages, int from, int userIndex)
    {
        var end = userIndex >= 0 ? userIndex : messages.Count - 1;
        if (end <= from) return null;

        var parts = new List<string>();
        var start = Math.Max(from, end - MaxHistoryMessages);
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
                case "document":
                    if (DocumentText(block) is { } document) builder.Append(document).Append('\n');
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

    // Pasted or attached text sent as a document block: plain text, or content made of text blocks. PDFs and other
    // binary sources are left out.
    private static string? DocumentText(JsonNode? block)
    {
        var source = block?["source"];
        var text = Str(source?["type"]) switch
        {
            "text" => Str(source?["data"]),
            "content" => source?["content"] is JsonArray parts
                ? string.Join("\n", parts.Select(p => Str(p?["text"])).OfType<string>())
                : Str(source?["content"]),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(text)) return null;

        var title = Str(block?["title"]);
        return $"<document{(string.IsNullOrWhiteSpace(title) ? string.Empty : $" title=\"{title}\"")}>\n{text.Trim()}\n</document>";
    }

    // Up to this many images survive per turn, and each one up to this many base64 chars (~6 MB raw), so a chat
    // full of screenshots cannot blow up the planner's request to 9router.
    private const int MaxImages = 6;
    private const int MaxImageBase64Chars = 8_000_000;

    // Canonical (Anthropic-shape) image block: { type: "image", source: { type: "base64", media_type, data } }.
    // ProtocolAdapter normalizes OpenAI/Gemini client images into this same shape before the inspector sees them.
    private static IReadOnlyList<ImageAttachment> ExtractImages(JsonNode? content)
    {
        if (content is not JsonArray blocks) return [];

        var images = new List<ImageAttachment>();
        foreach (var block in blocks)
        {
            if (images.Count >= MaxImages) break;
            if (!string.Equals(Str(block?["type"]), "image", StringComparison.OrdinalIgnoreCase)) continue;

            var source = block?["source"];
            if (!string.Equals(Str(source?["type"]), "base64", StringComparison.OrdinalIgnoreCase)) continue;

            var mediaType = Str(source?["media_type"]);
            var data = Str(source?["data"]);
            if (string.IsNullOrEmpty(mediaType) || string.IsNullOrEmpty(data) || data.Length > MaxImageBase64Chars) continue;

            images.Add(new ImageAttachment(mediaType, data));
        }
        return images;
    }

    private static string? Header(IReadOnlyDictionary<string, string?> headers, string name) =>
        headers.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static string? Str(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    // <system-notice> wraps a message the developer typed while the previous turn was still running.
    [GeneratedRegex(@"<(system-reminder|system-notice)>[\s\S]*?</\1>", RegexOptions.IgnoreCase)]
    private static partial Regex ReminderBlock();

    // Claude Code asks the main model, with the full tool list, to name the session (<title-request>) and to recap it
    // when the developer comes back (<recap>). Neither is a task for the planner.
    [GeneratedRegex(@"^<(?:title-request|recap)>", RegexOptions.IgnoreCase)]
    private static partial Regex SideRequest();

    // "Primary working directory:" is Claude Code, "working in the directory:" Gemini CLI, and oh-my-pi writes
    // "current working directory: '<path>'. Do not repeat…", so a quoted path ends at its closing quote. Codex puts
    // <cwd> in its environment context.
    [GeneratedRegex(@"(?:Primary working directory|Current working directory|Working directory|working in the directory|Current directory|Project directory|\bcwd)\s*[:=]\s*(?:'(?<path>[^'\r\n]+)'|""(?<path>[^""\r\n]+)""|`(?<path>[^`\r\n]+)`|(?<path>[^\r\n]+))", RegexOptions.IgnoreCase)]
    private static partial Regex WorkingDirectoryLine();

    [GeneratedRegex(@"<(?<tag>cwd|working_directory|current_working_directory)>\s*(?<path>[^<\r\n]+?)\s*</\k<tag>>", RegexOptions.IgnoreCase)]
    private static partial Regex WorkingDirectoryTag();

    [GeneratedRegex(@"Contents of\s+([^\r\n]+?)[\\/]CLAUDE\.md", RegexOptions.IgnoreCase)]
    private static partial Regex ClaudeMdLine();

    [GeneratedRegex(@"session_([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})")]
    private static partial Regex SessionPattern();
}
