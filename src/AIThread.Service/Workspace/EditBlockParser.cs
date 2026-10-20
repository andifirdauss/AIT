using System.Text.RegularExpressions;

namespace AIThread.Service.Workspace;

public sealed record EditBlock(string Search, string Replace)
{
    public bool IsWholeFile => Search.Trim().Length == 0;
}

public sealed record ParsedEdit(IReadOnlyList<EditBlock> Blocks, string? Summary, IReadOnlyList<string> Errors);

// Parses executor output made of Aider-style blocks:
//   <<<<<<< SEARCH
//   exact existing lines
//   =======
//   replacement lines
//   >>>>>>> REPLACE
//   SUMMARY: one sentence
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
                    break;

                case State.InSearch:
                    if (DividerMarker().IsMatch(line)) state = State.InReplace;
                    else if (SearchMarker().IsMatch(line)) errors.Add("A SEARCH block was opened twice without a ======= divider.");
                    else search.Add(line);
                    break;

                case State.InReplace:
                    if (ReplaceMarker().IsMatch(line))
                    {
                        blocks.Add(new EditBlock(string.Join('\n', search), string.Join('\n', replace)));
                        state = State.Outside;
                    }
                    else
                    {
                        replace.Add(line);
                    }
                    break;
            }
        }

        if (state != State.Outside) errors.Add("The last block is not closed with >>>>>>> REPLACE.");
        return new ParsedEdit(blocks, summary, errors);
    }

    private enum State
    {
        Outside,
        InSearch,
        InReplace
    }

    [GeneratedRegex(@"^\s*<{5,9}\s*SEARCH\s*$")]
    private static partial Regex SearchMarker();

    [GeneratedRegex(@"^\s*={5,9}\s*$")]
    private static partial Regex DividerMarker();

    [GeneratedRegex(@"^\s*>{5,9}\s*REPLACE\s*$")]
    private static partial Regex ReplaceMarker();

    [GeneratedRegex(@"^\s*\**SUMMARY\**\s*:\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryLine();
}
