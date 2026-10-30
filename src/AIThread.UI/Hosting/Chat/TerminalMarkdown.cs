using System.Text.RegularExpressions;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AIThread.UI.Hosting.Chat;

// The wrapper's answer (Markdown) drawn for the terminal: headings, lists, quotes, rules, tables and code blocks with
// Spectre.Console, inline marks through ChatRenderer.Inline. Anything else stays text; nothing is dropped.
public static partial class TerminalMarkdown
{
    public static IRenderable Render(string markdown)
    {
        var blocks = new List<IRenderable>();
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var blank = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();

            // One empty row between blocks, however many empty lines the text has.
            if (trimmed.Length == 0)
            {
                if (!blank && blocks.Count > 0) blocks.Add(new Text(string.Empty));
                blank = true;
                continue;
            }
            blank = false;

            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                var fence = trimmed[..3];
                var language = trimmed[3..].Trim();
                var code = new List<string>();
                for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith(fence, StringComparison.Ordinal); i++) code.Add(lines[i]);
                var panel = new Panel(new Text(string.Join('\n', code))).Border(BoxBorder.Rounded).BorderColor(Color.Grey);
                blocks.Add(language.Length > 0 ? panel.Header($"[grey]{Markup.Escape(language)}[/]") : panel);
                continue;
            }

            if (trimmed.StartsWith('|') && i + 1 < lines.Length && TableSeparator().IsMatch(lines[i + 1]))
            {
                var header = Cells(trimmed);
                var table = new Table().Border(TableBorder.Rounded).BorderColor(Color.Grey);
                foreach (var cell in header) table.AddColumn(new TableColumn(new Markup($"[bold]{ChatRenderer.Inline(cell, markup: true)}[/]")));
                for (i += 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
                {
                    var cells = Cells(lines[i].Trim());
                    table.AddRow(header.Select((_, c) => (IRenderable)new Markup(c < cells.Count ? ChatRenderer.Inline(cells[c], markup: true) : string.Empty)));
                }
                i--;
                blocks.Add(table);
                continue;
            }

            if (Heading().Match(trimmed) is { Success: true } heading)
            {
                var text = ChatRenderer.Inline(heading.Groups["text"].Value.Trim(), markup: true);
                blocks.Add(new Markup(heading.Groups["level"].Length <= 2 ? $"[bold cyan]{text}[/]" : $"[bold]{text}[/]"));
                continue;
            }

            if (HorizontalRule().IsMatch(trimmed))
            {
                blocks.Add(new Rule().RuleStyle("grey"));
                continue;
            }

            if (ListItem().Match(line) is { Success: true } item)
            {
                var marker = item.Groups["number"].Success ? item.Groups["number"].Value + "." : "•";
                var indent = item.Groups["indent"].Value.Replace("\t", "  ").Length;
                blocks.Add(Hanging(indent, $"[cyan]{Markup.Escape(marker)}[/]", ChatRenderer.Inline(item.Groups["text"].Value, markup: true)));
                continue;
            }

            if (trimmed.StartsWith('>'))
            {
                blocks.Add(Hanging(0, "[grey]▌[/]", $"[grey]{ChatRenderer.Inline(trimmed.TrimStart('>').Trim(), markup: true)}[/]"));
                continue;
            }

            blocks.Add(new Markup(ChatRenderer.Inline(line.TrimEnd(), markup: true)));
        }

        while (blocks.Count > 0 && blocks[^1] is Text) blocks.RemoveAt(blocks.Count - 1);
        return new Rows(blocks);
    }

    // A marker with the text beside it; wrapped lines start under the text, not under the marker.
    private static IRenderable Hanging(int indent, string marker, string text)
    {
        var grid = new Grid().AddColumn(new GridColumn().NoWrap().PadRight(1)).AddColumn();
        grid.AddRow(new Markup(marker), new Markup(text));
        return indent > 0 ? new Padder(grid, new Padding(indent, 0, 0, 0)) : grid;
    }

    private static List<string> Cells(string row)
    {
        var inner = row.Trim();
        if (inner.StartsWith('|')) inner = inner[1..];
        if (inner.EndsWith('|') && !inner.EndsWith("\\|", StringComparison.Ordinal)) inner = inner[..^1];
        return CellSplit().Split(inner).Select(c => c.Replace("\\|", "|").Trim()).ToList();
    }

    [GeneratedRegex(@"^(?<level>#{1,6})\s+(?<text>.+?)\s*#*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(?:-{3,}|\*{3,}|_{3,})$")]
    private static partial Regex HorizontalRule();

    [GeneratedRegex(@"^(?<indent>\s*)(?:[-*+]|(?<number>\d{1,3})[.)])\s+(?<text>.*)$")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^\s*\|?\s*:?-{2,}:?\s*(?:\|\s*:?-{2,}:?\s*)*\|?\s*$")]
    private static partial Regex TableSeparator();

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex CellSplit();
}
