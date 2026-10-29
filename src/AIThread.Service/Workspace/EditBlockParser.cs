using System.Text.RegularExpressions;

namespace AIThread.Service.Workspace;

public sealed record EditBlock(string Search, string Replace)
{
    public bool IsWholeFile => Search.Trim().Length == 0;
}

// Errors: what is wrong with the output's format, for the executor's retry. Any error rejects the whole edit, also when
// some blocks parsed: applying only part of the change would leave the file half done.
public sealed record ParsedEdit(IReadOnlyList<EditBlock> Blocks, string? Summary, IReadOnlyList<string> Errors);

// Parses executor output made of Aider-style blocks:
//   <<<<<<< SEARCH
//   exact existing lines
//   =======
//   replacement lines
//   >>>>>>> REPLACE
//   SUMMARY: one sentence
// Cheap models vary the markers: text after them ("<<<<<<< SEARCH (line 69)"), bold or a bullet around them,
// ORIGINAL/UPDATED instead of SEARCH/REPLACE, the closing marker glued to the last code line, a ``` fence inside a
// section. Those are read as meant; anything else is reported with what went wrong.
public static partial class EditBlockParser
{
    public static ParsedEdit Parse(string output)
    {
        var lines = TextFileIo.NormalizeNewLines(output).Split('\n');
        var blocks = new List<EditBlock>();
        var errors = new List<string>();
        string? summary = null;

        var state = State.Outside;
        var search = new List<string>();
        var replace = new List<string>();

        foreach (var line in lines)
        {
            switch (state)
            {
                case State.Outside:
                    if (SearchMarker().IsMatch(line))
                    {
                        state = State.InSearch;
                        search.Clear();
                        replace.Clear();
                    }
                    else if (SummaryLine().Match(line) is { Success: true } match)
                    {
                        summary = match.Groups[1].Value.Trim();
                    }
                    else if (ReplaceMarker().IsMatch(line))
                    {
                        // A stray ======= between blocks is harmless; a stray REPLACE means a block lost its SEARCH line.
                        errors.Add($"Block #{blocks.Count + 1}: a >>>>>>> REPLACE marker appears without its <<<<<<< SEARCH line.");
                    }
                    break;

                case State.InSearch:
                    if (DividerMarker().IsMatch(line)) state = State.InReplace;
                    else if (SearchMarker().IsMatch(line)) errors.Add($"Block #{blocks.Count + 1}: a SEARCH block was opened twice without a ======= divider.");
                    else if (ReplaceMarker().IsMatch(line))
                    {
                        errors.Add($"Block #{blocks.Count + 1}: the ======= divider between the SEARCH and the REPLACE part is missing.");
                        state = State.Outside;
                    }
                    else search.Add(line);
                    break;

                case State.InReplace:
                    if (ReplaceMarker().Match(line) is { Success: true } end)
                    {
                        // ">>>>>>> REPLACE" glued to the last code line: that code belongs to the replacement.
                        var glued = end.Groups["code"].Value;
                        if (glued.Trim().Trim('*', '-', '`').Length > 0) replace.Add(glued.TrimEnd());
                        blocks.Add(new EditBlock(string.Join('\n', Unfence(search)), string.Join('\n', Unfence(replace))));
                        state = State.Outside;
                    }
                    else if (SearchMarker().IsMatch(line))
                    {
                        errors.Add($"Block #{blocks.Count + 1}: it is not closed with >>>>>>> REPLACE before the next <<<<<<< SEARCH.");
                        state = State.InSearch;
                        search.Clear();
                        replace.Clear();
                    }
                    else
                    {
                        replace.Add(line);
                    }
                    break;
            }
        }

        if (state != State.Outside) errors.Add($"Block #{blocks.Count + 1}: the last block is not closed with >>>>>>> REPLACE (was the output cut off?).");
        if (blocks.Count == 0 && errors.Count == 0) errors.Add(Diagnose(lines));
        return new ParsedEdit(blocks, summary, errors);
    }

    // Why an output without any marker is not an edit, in words the model can act on.
    private static string Diagnose(string[] lines)
    {
        var text = string.Join('\n', lines).Trim();
        if (text.Length == 0)
            return "The reply was empty (a reasoning model may have spent all tokens thinking). Write the edit blocks in the reply itself.";
        if (lines.Any(l => l.StartsWith("@@", StringComparison.Ordinal)) || lines.Any(l => l.StartsWith("--- a/", StringComparison.Ordinal) || l.StartsWith("+++ b/", StringComparison.Ordinal)))
            return "The reply is a unified diff (@@, ---/+++ lines). AIT only applies SEARCH/REPLACE blocks.";
        if (lines.Any(l => l.TrimStart().StartsWith("<<<<", StringComparison.Ordinal) || l.TrimStart().StartsWith(">>>>", StringComparison.Ordinal)))
            return "The markers are malformed: write them on their own lines exactly as <<<<<<< SEARCH, ======= and >>>>>>> REPLACE (7 characters each).";
        if (lines.Count(l => l.TrimStart().StartsWith("```", StringComparison.Ordinal)) >= 2)
            return "The reply holds a code block without SEARCH/REPLACE markers. Do not rewrite the file: wrap each change in a SEARCH/REPLACE block.";
        return "The reply has no SEARCH/REPLACE block, only text. Do not explain or ask: output the edit blocks.";
    }

    // A section wrapped in a ``` fence ("```csharp" ... "```"): the fence is markup, not file content.
    private static List<string> Unfence(List<string> section)
    {
        var first = section.FindIndex(l => l.Trim().Length > 0);
        var last = section.FindLastIndex(l => l.Trim().Length > 0);
        if (first < 0 || first == last || !FenceLine().IsMatch(section[first]) || section[last].Trim() != "```") return section;
        return section.GetRange(first + 1, last - first - 1);
    }

    private enum State
    {
        Outside,
        InSearch,
        InReplace
    }

    // Optional bullet/bold around the marker, optional text after the keyword.
    [GeneratedRegex(@"^\s*(?:[-*]\s+)?\**\s*<{5,9}\s*(?:SEARCH|ORIGINAL)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex SearchMarker();

    [GeneratedRegex(@"^\s*(?:[-*]\s+)?\**\s*={5,9}\s*\**\s*$")]
    private static partial Regex DividerMarker();

    // "code" is what precedes the marker on its line: markup, or code the model glued to the marker.
    [GeneratedRegex(@"^(?<code>.*?)>{5,9}\s*(?:REPLACE|UPDATED)\b.*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReplaceMarker();

    [GeneratedRegex(@"^\s*\**SUMMARY\**\s*:\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryLine();

    [GeneratedRegex(@"^\s*```[\w#+.-]*\s*$")]
    private static partial Regex FenceLine();
}
