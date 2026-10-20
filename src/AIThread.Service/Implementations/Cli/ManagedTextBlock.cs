namespace AIThread.Service.Implementations.Cli;

// The lines AIThread owns in a config file whose comments start with '#' (YAML, TOML, .env), between two comment
// markers. Every line outside the markers stays as the user wrote it, so removing the block restores the file.
public static class ManagedTextBlock
{
    public const string Begin = "# >>> AIThread (dikelola otomatis, lepas lewat menu CLI) >>>";
    public const string End = "# <<< AIThread <<<";

    // Replaces any earlier block with one at the end of the file, after a blank line.
    public static string Append(string? content, IReadOnlyList<string> blockLines)
    {
        var newLine = NewLine(content);
        var lines = Lines(Remove(content ?? string.Empty));
        if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add(string.Empty);
        lines.Add(Begin);
        lines.AddRange(blockLines);
        lines.Add(End);
        return string.Join(newLine, lines) + newLine;
    }

    public static string Remove(string content)
    {
        var lines = Lines(content);
        var start = lines.FindIndex(l => IsMarker(l, Begin));
        if (start < 0) return content;

        var end = lines.FindIndex(start, l => IsMarker(l, End));
        if (end < 0) end = lines.Count - 1;

        lines.RemoveRange(start, end - start + 1);
        // The blank line Append put in front of a block at the end of the file.
        if (start > 0 && start == lines.Count && lines[start - 1].Trim().Length == 0) lines.RemoveAt(start - 1);
        return lines.Count == 0 ? string.Empty : string.Join(NewLine(content), lines) + NewLine(content);
    }

    // The lines inside the block, or null when there is none.
    public static IReadOnlyList<string>? Managed(string content)
    {
        var lines = Lines(content);
        var start = lines.FindIndex(l => IsMarker(l, Begin));
        if (start < 0) return null;
        var end = lines.FindIndex(start, l => IsMarker(l, End));
        return lines.Skip(start + 1).Take((end < 0 ? lines.Count : end) - start - 1).ToList();
    }

    // Ignores case so blocks written before the rename ("# >>> AiThread ...") are still found and replaced.
    private static bool IsMarker(string line, string marker) => string.Equals(line.Trim(), marker, StringComparison.OrdinalIgnoreCase);

    // The user's own lines: the file without the block.
    public static List<string> Outside(string content) => Lines(Remove(content));

    public static List<string> Lines(string content) =>
        content.Length == 0 ? [] : content.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();

    public static string NewLine(string? content) =>
        content is not null && content.Contains("\r\n") ? "\r\n" : content is { Length: > 0 } ? "\n" : Environment.NewLine;
}
