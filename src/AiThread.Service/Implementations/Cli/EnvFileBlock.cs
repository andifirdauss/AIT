using System.Text;
using System.Text.RegularExpressions;

namespace AiThread.Service.Implementations.Cli;

// Owns one delimited block inside a .env file, leaving every other line exactly as the user wrote it.
public static partial class EnvFileBlock
{
    public const string Begin = "# >>> AiThread (dikelola otomatis, lepas lewat menu CLI) >>>";
    public const string End = "# <<< AiThread <<<";

    // The block as it would be written, without a trailing newline.
    public static string Render(IEnumerable<KeyValuePair<string, string>> entries, string newLine) =>
        string.Join(newLine, new[] { Begin }.Concat(entries.Select(e => $"{e.Key}={e.Value}")).Append(End));

    // Replaces the block (or appends it), keeping the file's line ending style and everything outside the block.
    public static string Apply(string? content, IEnumerable<KeyValuePair<string, string>> entries)
    {
        var text = Remove(content ?? string.Empty);
        var newLine = NewLine(content);
        var builder = new StringBuilder(text);
        if (builder.Length > 0)
        {
            if (!text.EndsWith('\n')) builder.Append(newLine);
            builder.Append(newLine);
        }
        return builder.Append(Render(entries, newLine)).Append(newLine).ToString();
    }

    // Removes the block and the blank line that separated it; a file that only held the block becomes empty.
    public static string Remove(string content)
    {
        var lines = Lines(content);
        var start = lines.FindIndex(l => l.Trim() == Begin);
        if (start < 0) return content;

        var end = lines.FindIndex(start, l => l.Trim() == End);
        if (end < 0) end = lines.Count - 1;

        lines.RemoveRange(start, end - start + 1);
        if (start > 0 && start - 1 < lines.Count && lines[start - 1].Trim().Length == 0) lines.RemoveAt(start - 1);
        return lines.Count == 0 ? string.Empty : string.Join(NewLine(content), lines) + NewLine(content);
    }

    // Value of a key inside the managed block, or null.
    public static string? GetManaged(string content, string key)
    {
        var lines = Lines(content);
        var start = lines.FindIndex(l => l.Trim() == Begin);
        if (start < 0) return null;

        for (var i = start + 1; i < lines.Count && lines[i].Trim() != End; i++)
        {
            if (Assignment(lines[i]) is { } pair && pair.Key == key) return pair.Value;
        }
        return null;
    }

    // Value the user set for a key outside the managed block, or null. A second definition there would either
    // shadow the block or be shadowed by it, so the caller refuses to write rather than guess which one wins.
    public static string? GetUnmanaged(string content, string key)
    {
        var inBlock = false;
        foreach (var line in Lines(content))
        {
            var trimmed = line.Trim();
            if (trimmed == Begin) { inBlock = true; continue; }
            if (trimmed == End) { inBlock = false; continue; }
            if (!inBlock && Assignment(line) is { } pair && pair.Key == key) return pair.Value;
        }
        return null;
    }

    private static KeyValuePair<string, string>? Assignment(string line)
    {
        var match = AssignmentLine().Match(line);
        return match.Success ? new KeyValuePair<string, string>(match.Groups["key"].Value, match.Groups["value"].Value.Trim()) : null;
    }

    private static List<string> Lines(string content) =>
        content.Length == 0 ? [] : content.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();

    private static string NewLine(string? content) =>
        content is not null && content.Contains("\r\n") ? "\r\n" : content is { Length: > 0 } ? "\n" : Environment.NewLine;

    [GeneratedRegex(@"^\s*(?:export\s+)?(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=(?<value>.*)$")]
    private static partial Regex AssignmentLine();
}
