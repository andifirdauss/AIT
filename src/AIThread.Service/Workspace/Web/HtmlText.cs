using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AIThread.Service.Workspace.Web;

// A web page as readable text for the model: scripts, styles, navigation and graphics dropped; headings, list items,
// links, code and preformatted blocks kept as simple Markdown; entities decoded. The page's <main> (or <article>) is
// preferred over the whole body when it holds the content.
public static partial class HtmlText
{
    private const char Hole = '\u0001';

    public static string ToMarkdown(string html, Uri? baseUri, out string? title)
    {
        title = null;
        try
        {
            return Convert(html, baseUri, out title);
        }
        catch (RegexMatchTimeoutException)
        {
            // A pathological page: tags stripped without structure rather than nothing.
            return Collapse(WebUtility.HtmlDecode(TagRegex().Replace(DropRegex().Replace(html, " "), " ")));
        }
    }

    private static string Convert(string html, Uri? baseUri, out string? title)
    {
        var titleMatch = TitleRegex().Match(html);
        title = titleMatch.Success ? Inline(titleMatch.Groups[1].Value) : null;
        if (string.IsNullOrEmpty(title)) title = null;

        // Pages served with CRLF (or CR) keep the same Markdown as LF ones, preformatted blocks included.
        html = html.ReplaceLineEndings("\n");
        html = CommentRegex().Replace(html, " ");
        html = DropRegex().Replace(html, " ");
        var main = MainRegex().Match(html);
        if (main.Success && TagRegex().Replace(main.Groups[2].Value, string.Empty).Trim().Length >= 200) html = main.Groups[2].Value;
        else if (BodyRegex().Match(html) is { Success: true } body) html = body.Groups[1].Value;

        // Preformatted blocks keep their line breaks: set aside until the whitespace is collapsed.
        var blocks = new List<string>();
        html = PreRegex().Replace(html, m =>
        {
            var code = WebUtility.HtmlDecode(TagRegex().Replace(BreakRegex().Replace(m.Groups[1].Value, "\n"), string.Empty)).Trim('\n', '\r');
            blocks.Add($"\n\n```\n{code}\n```\n\n");
            return $"{Hole}{blocks.Count - 1}{Hole}";
        });

        html = LinkRegex().Replace(html, m => Link(m, baseUri));
        html = CodeRegex().Replace(html, m => Inline(m.Groups[1].Value) is { Length: > 0 } code ? $"`{code}`" : string.Empty);
        html = HeadingRegex().Replace(html, m => $"\n\n{new string('#', m.Groups[1].Value[0] - '0')} {Inline(m.Groups[2].Value)}\n\n");
        html = BreakRegex().Replace(html, "\n");
        html = RuleRegex().Replace(html, "\n\n---\n\n");
        html = ItemRegex().Replace(html, "\n- ");
        html = CellRegex().Replace(html, " | ");
        html = BlockRegex().Replace(html, "\n");
        html = TagRegex().Replace(html, string.Empty);

        var text = Collapse(WebUtility.HtmlDecode(html));
        return HoleRegex().Replace(text, m => blocks[int.Parse(m.Groups[1].Value)]).Trim();
    }

    private static string Link(Match match, Uri? baseUri)
    {
        var text = Inline(match.Groups["text"].Value);
        var href = WebUtility.HtmlDecode(match.Groups["href"].Value).Trim();
        if (href.Length == 0 || href.StartsWith('#') || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) return text;
        if (baseUri is not null && Uri.TryCreate(baseUri, href, out var absolute)) href = absolute.ToString();
        if (text.Length == 0) return string.Empty;
        return text == href ? $"<{href}>" : $"[{text}]({href})";
    }

    // The text of an inline fragment on one line.
    private static string Inline(string fragment) =>
        SpacesRegex().Replace(WebUtility.HtmlDecode(TagRegex().Replace(fragment, " ")), " ").Trim();

    // Spaces within a line collapse to one, blank lines to at most one.
    private static string Collapse(string text)
    {
        var builder = new StringBuilder(text.Length);
        var blank = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = SpacesRegex().Replace(raw, " ").Trim();
            if (line is "|" or "| |") line = string.Empty;
            if (line.Length == 0)
            {
                if (builder.Length > 0 && ++blank == 1) builder.Append('\n');
                continue;
            }
            blank = 0;
            builder.Append(line).Append('\n');
        }
        return builder.ToString();
    }

    [GeneratedRegex(@"<title\b[^>]*>(.*?)</title\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline, 2000)]
    private static partial Regex CommentRegex();

    [GeneratedRegex(@"<(script|style|noscript|svg|nav|template|iframe|head|footer|form|button|select|canvas)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex DropRegex();

    [GeneratedRegex(@"<(main|article)\b[^>]*>(.*)</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex MainRegex();

    [GeneratedRegex(@"<body\b[^>]*>(.*)</body\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex BodyRegex();

    [GeneratedRegex(@"<pre\b[^>]*>(.*?)</pre\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex PreRegex();

    [GeneratedRegex("""<a\b[^>]*?\bhref\s*=\s*(?:"(?<href>[^"]*)"|'(?<href>[^']*)'|(?<href>[^\s>]+))[^>]*>(?<text>.*?)</a\s*>""", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex LinkRegex();

    [GeneratedRegex(@"<code\b[^>]*>(.*?)</code\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"<h([1-6])\b[^>]*>(.*?)</h\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline, 2000)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"<br\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex BreakRegex();

    [GeneratedRegex(@"<hr\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex RuleRegex();

    [GeneratedRegex(@"<li\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex ItemRegex();

    [GeneratedRegex(@"<t[dh]\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex CellRegex();

    [GeneratedRegex(@"</?(p|div|section|article|main|header|aside|ul|ol|li|table|thead|tbody|tr|blockquote|dl|dt|dd|figure|figcaption|details|summary)\b[^>]*>", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex BlockRegex();

    [GeneratedRegex(@"<[^>]*>", RegexOptions.None, 2000)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"[ \t\f\v ]+")]
    private static partial Regex SpacesRegex();

    [GeneratedRegex("\u0001(\\d+)\u0001")]
    private static partial Regex HoleRegex();
}
