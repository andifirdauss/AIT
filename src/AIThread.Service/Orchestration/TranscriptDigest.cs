using System.Text.Json;
using System.Text.Json.Nodes;
using AIThread.Service.Proxy;

namespace AIThread.Service.Orchestration;

// Requests are the developer's prompts (one line each); FilesChanged are the targets of edit tools, relative to the
// working directory when inside it.
public sealed record TranscriptSummary(string Digest, IReadOnlyList<string> Requests, IReadOnlyList<string> FilesChanged, int Entries);

// Bounded text of a Claude Code transcript (JSONL) for the context keeper: the developer's prompts, the assistant's
// text, one line per tool call, tool errors and compaction summaries. Tool results, thinking and sub-agent traffic are
// left out — they are what makes transcripts large.
public static class TranscriptDigest
{
    public const int MaxDigestChars = 60_000;

    private static readonly string[] EditTools = ["Edit", "MultiEdit", "Write", "NotebookEdit"];
    private static readonly string[] DescribedInputs = ["file_path", "notebook_path", "path", "pattern", "command", "url", "query", "description"];

    public static TranscriptSummary Build(IEnumerable<string> lines, string? workingDirectory)
    {
        var parts = new List<string>();
        var requests = new List<string>();
        var files = new List<string>();
        var entries = 0;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line) || line[0] != '{') continue;
            JsonObject? evt;
            try
            {
                evt = JsonNode.Parse(line) as JsonObject;
            }
            catch (JsonException)
            {
                continue;
            }
            if (evt is null || Bool(evt["isSidechain"]) || Bool(evt["isMeta"])) continue;

            var before = parts.Count;
            switch (Str(evt["type"]))
            {
                case "user":
                    AddUser(evt, parts, requests);
                    break;
                case "assistant":
                    AddAssistant(evt, parts, files, workingDirectory);
                    break;
                case "summary" when Str(evt["summary"]) is { Length: > 0 } summary:
                    parts.Add($"[summary] {Shorten(summary, 2_000)}");
                    break;
            }
            if (parts.Count > before) entries++;
        }

        return new TranscriptSummary(Fit(parts), requests, files, entries);
    }

    private static void AddUser(JsonObject evt, List<string> parts, List<string> requests)
    {
        var content = evt["message"]?["content"];
        if (Bool(evt["isCompactSummary"]))
        {
            parts.Add($"[compacted conversation summary]\n{Shorten(Text(content), 8_000)}");
            return;
        }

        var text = string.Empty;
        if (content is JsonArray blocks)
        {
            foreach (var block in blocks)
            {
                switch (Str(block?["type"]))
                {
                    case "text":
                        text += Str(block?["text"]) + "\n";
                        break;
                    case "tool_result" when Bool(block?["is_error"]):
                        parts.Add($"[tool error] {Shorten(OneLine(Text(block?["content"])), 300)}");
                        break;
                }
            }
        }
        else
        {
            text = Str(content) ?? string.Empty;
        }

        text = ClaudeRequestInspector.StripReminders(text).Trim();
        // Slash-command and local-command output wrappers are UI artifacts, not requests.
        if (text.Length == 0 || text.StartsWith("<command-", StringComparison.Ordinal) || text.StartsWith("<local-command-", StringComparison.Ordinal))
            return;

        requests.Add(Shorten(OneLine(text), 300));
        parts.Add($"[user] {Shorten(text, 1_500)}");
    }

    private static void AddAssistant(JsonObject evt, List<string> parts, List<string> files, string? workingDirectory)
    {
        if (evt["message"]?["content"] is not JsonArray blocks) return;
        foreach (var block in blocks)
        {
            switch (Str(block?["type"]))
            {
                case "text" when Str(block?["text"]) is { } text && text.Trim().Length > 0:
                    parts.Add($"[assistant] {Shorten(text.Trim(), 800)}");
                    break;
                case "tool_use":
                    var name = Str(block?["name"]) ?? "tool";
                    var input = block?["input"] as JsonObject;
                    parts.Add($"[tool] {name} {Describe(input, workingDirectory)}".TrimEnd());
                    if (EditTools.Contains(name) && (Str(input?["file_path"]) ?? Str(input?["notebook_path"])) is { } file)
                    {
                        var relative = Relative(file, workingDirectory);
                        if (!files.Contains(relative, StringComparer.OrdinalIgnoreCase)) files.Add(relative);
                    }
                    break;
            }
        }
    }

    private static string Describe(JsonObject? input, string? workingDirectory)
    {
        if (input is null) return string.Empty;
        foreach (var key in DescribedInputs)
        {
            if (Str(input[key]) is not { Length: > 0 } value) continue;
            return key is "file_path" or "notebook_path" or "path" ? Relative(value, workingDirectory) : Shorten(OneLine(value), 200);
        }
        return string.Empty;
    }

    // Keeps the first request (usually the goal) and as much of the end as fits.
    private static string Fit(List<string> parts)
    {
        var all = string.Join("\n", parts);
        if (all.Length <= MaxDigestChars) return all;

        var head = parts.FirstOrDefault(p => p.StartsWith("[user]", StringComparison.Ordinal)) ?? string.Empty;
        // Room for the omission marker line.
        var budget = MaxDigestChars - head.Length - 60;
        var tail = new List<string>();
        for (var i = parts.Count - 1; i >= 0 && budget - parts[i].Length - 1 >= 0; i--)
        {
            tail.Insert(0, parts[i]);
            budget -= parts[i].Length + 1;
        }
        return $"{head}\n… (earlier part of the session omitted) …\n{string.Join("\n", tail)}";
    }

    private static string Relative(string path, string? workingDirectory)
    {
        if (string.IsNullOrEmpty(workingDirectory)) return path;
        var root = workingDirectory.TrimEnd('\\', '/');
        return path.Length > root.Length + 1 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && path[root.Length] is '\\' or '/'
            ? path[(root.Length + 1)..].Replace('\\', '/')
            : path;
    }

    // Plain text of a message or tool result content: a string or an array of text blocks.
    private static string Text(JsonNode? content) => content switch
    {
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonArray blocks => string.Join("\n", blocks.Select(b => Str(b?["text"])).Where(t => !string.IsNullOrEmpty(t))),
        _ => string.Empty
    };

    private static string OneLine(string text) => text.ReplaceLineEndings(" ").Trim();

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..max] + " …";

    private static bool Bool(JsonNode? node) => node is JsonValue value && value.TryGetValue<bool>(out var b) && b;

    private static string? Str(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
