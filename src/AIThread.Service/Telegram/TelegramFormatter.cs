using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AIThread.Service.Telegram;

// Converts the Markdown written by the wrapper into the HTML subset Telegram accepts (b, i, s, code, pre, a) and cuts
// it into messages. Everything else is escaped and code never ends up inside bold/italic (Telegram rejects that), so
// the output is well-formed; if Telegram still rejects a chunk, TelegramApi resends it as plain text (ToPlainText).
public static partial class TelegramFormatter
{
    // Telegram allows 4096 characters per message after entity parsing. Chunks are measured as HTML, which is always
    // longer than the visible text, so this keeps a margin.
    public const int MaxChunkLength = 3900;

    public static string Escape(string? text) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    public static string Truncate(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max) return text ?? string.Empty;
        var cut = max - 1;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1])) cut--;
        return text[..cut] + "…";
    }

    public static IReadOnlyList<string> ToChunks(string? markdown, int maxLength = MaxChunkLength) =>
        Pack(ToBlocks(markdown, maxLength), maxLength);

    // Joins HTML blocks into as few messages as possible without ever splitting a block. Blocks must fit maxLength.
    public static IReadOnlyList<string> Pack(IEnumerable<string> blocks, int maxLength = MaxChunkLength)
    {
        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var block in blocks)
        {
            if (current.Length > 0 && current.Length + 1 + block.Length > maxLength) Flush();
            if (current.Length > 0) current.Append('\n');
            current.Append(block);
        }
        Flush();
        return chunks;

        void Flush()
        {
            var text = current.ToString().Trim('\n');
            if (!string.IsNullOrWhiteSpace(text)) chunks.Add(text);
            current.Clear();
        }
    }

    // One block per rendered line; a fenced code block or a table becomes one or more <pre> blocks. Runs of blank
    // lines become one empty block, and none is emitted at the start or the end.
    public static IEnumerable<string> ToBlocks(string? markdown, int maxLength = MaxChunkLength)
    {
        if (string.IsNullOrWhiteSpace(markdown)) yield break;

        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var started = false;
        var pendingBlank = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                pendingBlank = started;
                continue;
            }

            IEnumerable<string> rendered;
            var fence = FenceOpen().Match(line);
            if (fence.Success)
            {
                var marker = fence.Groups["marker"].Value;
                var code = new List<string>();
                for (i++; i < lines.Length && !IsFenceClose(lines[i], marker); i++) code.Add(lines[i]);
                rendered = CodeBlocks(code, fence.Groups["lang"].Value, maxLength);
            }
            else if (IsTableRow(line))
            {
                var rows = new List<string>();
                for (; i < lines.Length && IsTableRow(lines[i]); i++) rows.Add(lines[i]);
                i--;
                rendered = CodeBlocks(FormatTable(rows), null, maxLength);
            }
            else
            {
                // A single enormous line is sent unformatted, in pieces.
                var html = RenderLine(line);
                rendered = html.Length <= maxLength ? [html] : SplitEscaped(line, maxLength);
            }

            if (pendingBlank) yield return string.Empty;
            pendingBlank = false;
            started = true;
            foreach (var block in rendered) yield return block;
        }
    }

    // Monospace block(s) for code, a diff or a table; split across several <pre> when longer than maxLength.
    public static IEnumerable<string> CodeBlocks(IEnumerable<string> lines, string? language, int maxLength = MaxChunkLength)
    {
        var (open, close) = string.IsNullOrEmpty(language)
            ? ("<pre>", "</pre>")
            : ($"<pre><code class=\"language-{language}\">", "</code></pre>");
        var budget = Math.Max(1, maxLength - open.Length - close.Length);
        var body = new StringBuilder();
        foreach (var line in lines)
        {
            foreach (var piece in SplitEscaped(line.TrimEnd(), budget))
            {
                if (body.Length > 0 && body.Length + 1 + piece.Length > budget)
                {
                    yield return open + body + close;
                    body.Clear();
                }
                if (body.Length > 0) body.Append('\n');
                body.Append(piece);
            }
        }
        if (body.Length > 0) yield return open + body + close;
    }

    // Inline Markdown (code spans, links, bold, italic, strikethrough) → Telegram HTML. Code spans and links are
    // rendered first and parked, so emphasis rules never touch their content or URL.
    public static string InlineMarkdown(string text)
    {
        var parked = new List<string>();

        var working = CodeSpan().Replace(text, m => Park($"<code>{Escape(m.Groups["code"].Value.Trim())}</code>"));
        working = Link().Replace(working, m =>
            Park($"<a href=\"{Escape(m.Groups["url"].Value).Replace("\"", "&quot;")}\">{Escape(m.Groups["label"].Value)}</a>"));

        var html = Escape(working);
        html = BoldStars().Replace(html, "<b>${text}</b>");
        html = BoldUnderscores().Replace(html, "<b>${text}</b>");
        html = Strike().Replace(html, "<s>${text}</s>");
        html = ItalicStar().Replace(html, "<i>${text}</i>");
        html = ItalicUnderscore().Replace(html, "<i>${text}</i>");

        // Twice: a parked link label may itself contain a parked code span.
        for (var pass = 0; pass < 2 && html.Contains('\u0001'); pass++)
            html = Parked().Replace(html, m => parked[int.Parse(m.Groups["index"].Value)]);

        return Normalize(html);

        string Park(string rendered)
        {
            parked.Add(rendered);
            return $"\u0001{parked.Count - 1}\u0002";
        }
    }

    // What Telegram shows when the HTML is rejected: tags removed, links spelled out, entities decoded.
    public static string ToPlainText(string html)
    {
        var text = LinkTag().Replace(html, m => $"{m.Groups["label"].Value} ({m.Groups["url"].Value})");
        return WebUtility.HtmlDecode(AnyTag().Replace(text, string.Empty));
    }

    private static string RenderLine(string line)
    {
        var heading = Heading().Match(line);
        if (heading.Success) return Normalize($"<b>{InlineMarkdown(heading.Groups["text"].Value.Replace("**", string.Empty))}</b>");

        if (Rule().IsMatch(line)) return "──────────";

        var quote = Quote().Match(line);
        if (quote.Success) return "┃ " + InlineMarkdown(quote.Groups["text"].Value);

        var bullet = Bullet().Match(line);
        if (bullet.Success)
        {
            var indent = Indent(bullet.Groups["indent"].Value);
            var text = bullet.Groups["text"].Value;
            var box = TaskBox().Match(text);
            return box.Success
                ? $"{indent}{(box.Groups["mark"].Value == " " ? "☐" : "☑")} {InlineMarkdown(box.Groups["text"].Value)}"
                : $"{indent}• {InlineMarkdown(text)}";
        }

        var numbered = Numbered().Match(line);
        if (numbered.Success)
            return $"{Indent(numbered.Groups["indent"].Value)}{numbered.Groups["n"].Value}. {InlineMarkdown(numbered.Groups["text"].Value)}";

        return InlineMarkdown(line.TrimEnd());
    }

    // Telegram forbids code inside bold/italic/strikethrough: close the open emphasis around every <code>, reopen it
    // after, then drop the empty pairs this leaves behind.
    private static string Normalize(string html)
    {
        if (!html.Contains("<code>")) return html;

        var open = new List<string>();
        var result = new StringBuilder();
        var position = 0;
        foreach (Match tag in InlineTag().Matches(html))
        {
            result.Append(html, position, tag.Index - position);
            position = tag.Index + tag.Length;
            var closing = tag.Groups["close"].Success;
            var name = tag.Groups["name"].Value;

            if (name == "code")
            {
                if (!closing) for (var i = open.Count - 1; i >= 0; i--) result.Append("</").Append(open[i]).Append('>');
                result.Append(tag.Value);
                if (closing) foreach (var reopened in open) result.Append('<').Append(reopened).Append('>');
                continue;
            }

            if (!closing) open.Add(name);
            else if (open.LastIndexOf(name) is var index and >= 0) open.RemoveAt(index);
            result.Append(tag.Value);
        }
        result.Append(html, position, html.Length - position);

        var normalized = result.ToString();
        string previous;
        do
        {
            previous = normalized;
            normalized = EmptyEmphasis().Replace(normalized, string.Empty);
        } while (normalized != previous);
        return normalized;
    }

    // Nested list levels keep their indentation (two spaces per level, at most four levels).
    private static string Indent(string whitespace) => new(' ', Math.Min(8, whitespace.Replace("\t", "  ").Length));

    private static bool IsFenceClose(string line, string marker)
    {
        var trimmed = line.Trim();
        return trimmed.Length >= marker.Length && trimmed.All(c => c == marker[0]);
    }

    private static bool IsTableRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('|') && trimmed.Count(c => c == '|') >= 2;
    }

    // Aligns the columns for a monospace block; inline markers are dropped since <pre> cannot hold formatting.
    private static List<string> FormatTable(List<string> rows)
    {
        var cells = rows
            .Select(r => r.Trim().Trim('|').Split('|').Select(c => c.Trim().Replace("**", string.Empty).Replace("`", string.Empty)).ToList())
            .ToList();
        var separatorIndex = cells.FindIndex(r => r.All(c => TableSeparatorCell().IsMatch(c)));
        if (separatorIndex >= 0) cells.RemoveAt(separatorIndex);
        if (cells.Count == 0) return [];

        var columns = cells.Max(r => r.Count);
        var widths = Enumerable.Range(0, columns).Select(c => cells.Max(r => c < r.Count ? r[c].Length : 0)).ToArray();
        string Row(List<string> row) =>
            string.Join(" │ ", Enumerable.Range(0, columns).Select(c => (c < row.Count ? row[c] : string.Empty).PadRight(widths[c]))).TrimEnd();

        var lines = new List<string>();
        for (var r = 0; r < cells.Count; r++)
        {
            lines.Add(Row(cells[r]));
            if (r == 0 && separatorIndex == 1) lines.Add(string.Join("─┼─", widths.Select(w => new string('─', w))));
        }
        return lines;
    }

    // Escapes text and cuts it into pieces whose escaped length fits max, never inside an entity or surrogate pair.
    private static IEnumerable<string> SplitEscaped(string text, int max)
    {
        if (text.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        var piece = new StringBuilder();
        foreach (var c in text)
        {
            var escaped = c switch { '&' => "&amp;", '<' => "&lt;", '>' => "&gt;", _ => null };
            if (piece.Length > 0 && piece.Length + (escaped?.Length ?? 1) > max && !char.IsLowSurrogate(c))
            {
                yield return piece.ToString();
                piece.Clear();
            }
            if (escaped is null) piece.Append(c);
            else piece.Append(escaped);
        }
        if (piece.Length > 0) yield return piece.ToString();
    }

    [GeneratedRegex(@"^\s{0,3}(?<marker>`{3,}|~{3,})\s*(?<lang>[A-Za-z0-9_+#.-]*)")]
    private static partial Regex FenceOpen();

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(?<text>.*?)(?:\s+#+)?\s*$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^\s{0,3}([-*_])(\s*\1){2,}\s*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"^\s{0,3}>\s?(?<text>.*)$")]
    private static partial Regex Quote();

    [GeneratedRegex(@"^(?<indent>\s*)[-*+]\s+(?<text>.*)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"^(?<indent>\s*)(?<n>\d{1,3})[.)]\s+(?<text>.*)$")]
    private static partial Regex Numbered();

    [GeneratedRegex(@"^\[(?<mark>[ xX])\]\s+(?<text>.*)$")]
    private static partial Regex TaskBox();

    [GeneratedRegex(@"(?<ticks>`+)(?<code>.+?)\k<ticks>")]
    private static partial Regex CodeSpan();

    [GeneratedRegex(@"\[(?<label>[^\]\n]+)\]\((?<url>https?://[^\s)""]+)\)")]
    private static partial Regex Link();

    [GeneratedRegex(@"\*\*(?=\S)(?<text>.+?)(?<=\S)\*\*")]
    private static partial Regex BoldStars();

    [GeneratedRegex(@"(?<!\w)__(?=\S)(?<text>.+?)(?<=\S)__(?!\w)")]
    private static partial Regex BoldUnderscores();

    [GeneratedRegex(@"~~(?=\S)(?<text>.+?)(?<=\S)~~")]
    private static partial Regex Strike();

    [GeneratedRegex(@"(?<![\w*])\*(?=[^\s*])(?<text>.+?)(?<=[^\s*])\*(?![\w*])")]
    private static partial Regex ItalicStar();

    [GeneratedRegex(@"(?<!\w)_(?=[^\s_])(?<text>.+?)(?<=[^\s_])_(?!\w)")]
    private static partial Regex ItalicUnderscore();

    [GeneratedRegex("\u0001(?<index>\\d+)\u0002")]
    private static partial Regex Parked();

    [GeneratedRegex(@"<(?<close>/)?(?<name>b|i|s|code)>")]
    private static partial Regex InlineTag();

    [GeneratedRegex(@"<(b|i|s)></\1>")]
    private static partial Regex EmptyEmphasis();

    [GeneratedRegex(@"<a href=""(?<url>[^""]*)"">(?<label>.*?)</a>", RegexOptions.Singleline)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"</?[a-zA-Z][^>]*>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"^:?-+:?$")]
    private static partial Regex TableSeparatorCell();
}
