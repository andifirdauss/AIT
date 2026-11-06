using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AIThread.Service.Models;

namespace AIThread.Service.CliBackend;

// Reads the <tool_call name="…">{json}</tool_call> blocks of a CLI reply (the protocol written by CliTranscript) back
// into ToolCalls. A block inside a code fence is an example, not a call. A block whose content is not a JSON object
// stays in the text, so the agent sees a reply without a valid call and nudges the model the way it does for any
// model that ignores tool calling.
public static partial class CliToolCallParser
{
    public sealed record Parsed(string Content, IReadOnlyList<ToolCall> ToolCalls);

    public static Parsed Parse(string reply)
    {
        if (string.IsNullOrEmpty(reply) || !reply.Contains("<" + CliTranscript.ToolCallTag, StringComparison.Ordinal))
            return new Parsed(reply ?? string.Empty, []);

        var fences = FenceRanges(reply);
        var calls = new List<ToolCall>();
        var removed = new List<(int Start, int Length)>();
        foreach (Match match in ToolCallPattern().Matches(reply))
        {
            if (fences.Any(f => match.Index >= f.Start && match.Index < f.End)) continue;
            var arguments = Arguments(match.Groups["args"].Value);
            if (arguments is null) continue;
            calls.Add(new ToolCall(NewId(), match.Groups["name"].Value.Trim(), arguments));
            removed.Add((match.Index, match.Length));
        }
        if (calls.Count == 0) return new Parsed(reply, []);

        var content = reply;
        foreach (var (start, length) in removed.OrderByDescending(r => r.Start)) content = content.Remove(start, length);
        return new Parsed(content.Trim(), calls);
    }

    // The JSON object of a block, compact; null when it is not one. A code fence around it is tolerated.
    private static string? Arguments(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLine = text.IndexOf('\n');
            text = firstLine < 0 ? string.Empty : text[(firstLine + 1)..];
            var close = text.LastIndexOf("```", StringComparison.Ordinal);
            if (close >= 0) text = text[..close];
            text = text.Trim();
        }
        if (text.Length == 0) return "{}";
        try
        {
            return JsonNode.Parse(text) is JsonObject json ? json.ToJsonString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Ranges of ``` fenced blocks; an unclosed fence runs to the end.
    private static List<(int Start, int End)> FenceRanges(string text)
    {
        var ranges = new List<(int, int)>();
        int? open = null;
        var index = 0;
        foreach (var line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
            {
                if (open is null) open = index;
                else
                {
                    ranges.Add((open.Value, index + line.Length));
                    open = null;
                }
            }
            index += line.Length + 1;
        }
        if (open is not null) ranges.Add((open.Value, text.Length));
        return ranges;
    }

    // Unique across the conversation: the transcript maps result ids back to tool names.
    private static string NewId() => $"cli_{Guid.NewGuid():N}"[..20];

    [GeneratedRegex("""<tool_call\s+name\s*=\s*["'](?<name>[^"']+)["'][^>]*>(?<args>.*?)</tool_call>""", RegexOptions.Singleline)]
    private static partial Regex ToolCallPattern();
}
