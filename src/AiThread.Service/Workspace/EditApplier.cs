namespace AiThread.Service.Workspace;

public sealed record ApplyResult(bool Success, string Content, IReadOnlyList<string> Errors, bool CreatesFile);

// Applies SEARCH/REPLACE blocks in memory. All-or-nothing: the caller writes the file only when every block applied.
// Matching is tried from strict to lenient: exact text, then line-by-line ignoring trailing whitespace,
// then ignoring indentation (re-indenting the replacement). Ambiguous matches are rejected, never guessed.
public static class EditApplier
{
    public static ApplyResult Apply(TextFileState file, IReadOnlyList<EditBlock> blocks)
    {
        var content = file.Content;
        var errors = new List<string>();
        var createsFile = false;

        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var replace = TextFileIo.NormalizeNewLines(block.Replace);

            if (block.IsWholeFile)
            {
                if (!file.Exists || content.Trim().Length == 0)
                {
                    content = replace.EndsWith('\n') ? replace : replace + "\n";
                    createsFile = !file.Exists;
                }
                else
                {
                    errors.Add($"Block #{i + 1}: an empty SEARCH section is only allowed when creating a new file, but this file already exists. Copy the exact lines you want to change into SEARCH.");
                }
                continue;
            }

            var error = TryReplace(ref content, TextFileIo.NormalizeNewLines(block.Search), replace);
            if (error is not null) errors.Add($"Block #{i + 1}: {error}");
        }

        return new ApplyResult(errors.Count == 0, content, errors, createsFile);
    }

    private static string? TryReplace(ref string content, string search, string replace)
    {
        var occurrences = CountOccurrences(content, search);
        if (occurrences == 1)
        {
            var index = content.IndexOf(search, StringComparison.Ordinal);
            content = string.Concat(content.AsSpan(0, index), replace, content.AsSpan(index + search.Length));
            return null;
        }
        if (occurrences > 1)
            return $"the SEARCH text occurs {occurrences} times in the file; include more surrounding lines so it matches exactly once.";

        foreach (var ignoreIndentation in new[] { false, true })
        {
            var outcome = TryReplaceLines(ref content, search, replace, ignoreIndentation);
            if (outcome == LineMatch.Replaced) return null;
            if (outcome == LineMatch.Ambiguous)
                return "the SEARCH lines match several places in the file; include more surrounding lines so it matches exactly once.";
        }

        var firstLine = search.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? string.Empty;
        return $"the SEARCH text was not found. It must be copied exactly from the current file content. First SEARCH line: \"{Truncate(firstLine, 120)}\"";
    }

    private static LineMatch TryReplaceLines(ref string content, string search, string replace, bool ignoreIndentation)
    {
        var contentLines = content.Split('\n');
        var searchLines = TrimBlankEdges(search.Split('\n'));
        if (searchLines.Length == 0) return LineMatch.NotFound;

        Func<string, string> normalize = ignoreIndentation ? s => s.Trim() : s => s.TrimEnd();
        var normalizedSearch = searchLines.Select(normalize).ToArray();

        var matchIndex = -1;
        for (var i = 0; i + searchLines.Length <= contentLines.Length; i++)
        {
            var matched = true;
            for (var j = 0; j < searchLines.Length; j++)
            {
                if (normalize(contentLines[i + j]) != normalizedSearch[j])
                {
                    matched = false;
                    break;
                }
            }

            if (!matched) continue;
            if (matchIndex >= 0) return LineMatch.Ambiguous;
            matchIndex = i;
        }

        if (matchIndex < 0) return LineMatch.NotFound;

        var replaceLines = replace.Length == 0 ? [] : replace.Split('\n');
        if (ignoreIndentation) replaceLines = Reindent(replaceLines, searchLines[0], contentLines[matchIndex]);

        var result = new List<string>(contentLines.Length - searchLines.Length + replaceLines.Length);
        result.AddRange(contentLines.Take(matchIndex));
        result.AddRange(replaceLines);
        result.AddRange(contentLines.Skip(matchIndex + searchLines.Length));
        content = string.Join('\n', result);
        return LineMatch.Replaced;
    }

    // Shifts the replacement by the indentation difference between what the model wrote and the real file.
    private static string[] Reindent(string[] replaceLines, string searchFirstLine, string actualFirstLine)
    {
        var modelIndent = LeadingWhitespace(searchFirstLine);
        var actualIndent = LeadingWhitespace(actualFirstLine);
        if (modelIndent == actualIndent) return replaceLines;

        return replaceLines.Select(line =>
        {
            if (line.Trim().Length == 0) return line;
            if (modelIndent.Length > 0 && line.StartsWith(modelIndent, StringComparison.Ordinal))
                return actualIndent + line[modelIndent.Length..];
            return modelIndent.Length == 0 ? actualIndent + line : line;
        }).ToArray();
    }

    private static string[] TrimBlankEdges(string[] lines)
    {
        var start = 0;
        var end = lines.Length;
        while (start < end && lines[start].Trim().Length == 0) start++;
        while (end > start && lines[end - 1].Trim().Length == 0) end--;
        return lines[start..end];
    }

    private static int CountOccurrences(string content, string search)
    {
        if (search.Length == 0) return 0;
        var count = 0;
        var index = 0;
        while ((index = content.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }
        return count;
    }

    private static string LeadingWhitespace(string line) => line[..(line.Length - line.TrimStart().Length)];

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max] + "…";

    private enum LineMatch
    {
        NotFound,
        Replaced,
        Ambiguous
    }
}
