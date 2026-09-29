using AiThread.Service.Telegram;

namespace AiThread.Tests;

public class TelegramFormatterTests
{
    [Fact]
    public void Escape_replaces_the_characters_telegram_html_reserves()
    {
        Assert.Equal("a &lt; b &amp;&amp; c &gt; d", TelegramFormatter.Escape("a < b && c > d"));
    }

    [Fact]
    public void Inline_markdown_renders_bold_italic_code_and_links()
    {
        var html = TelegramFormatter.InlineMarkdown("**Bold** and *italic*, `a<b>` [docs](https://x.dev/a_b?x=1&y=2)");

        Assert.Equal("<b>Bold</b> and <i>italic</i>, <code>a&lt;b&gt;</code> <a href=\"https://x.dev/a_b?x=1&amp;y=2\">docs</a>", html);
    }

    [Fact]
    public void Inline_markdown_leaves_identifiers_globs_and_arithmetic_alone()
    {
        Assert.Equal("update user_name in *.cs files, 2 * 3 &amp; my__var__name",
            TelegramFormatter.InlineMarkdown("update user_name in *.cs files, 2 * 3 & my__var__name"));
    }

    [Fact]
    public void Code_is_moved_out_of_emphasis_because_telegram_rejects_code_inside_bold()
    {
        Assert.Equal("<b>Files: </b><code>a.cs</code><b> and </b><code>b.cs</code>",
            TelegramFormatter.InlineMarkdown("**Files: `a.cs` and `b.cs`**"));
    }

    [Fact]
    public void Heading_with_code_keeps_the_code_outside_the_bold()
    {
        Assert.Equal(["<b>Ubah </b><code>UserService</code>"], TelegramFormatter.ToBlocks("## Ubah `UserService`"));
    }

    [Fact]
    public void Blocks_render_headings_lists_rules_quotes_and_task_boxes()
    {
        var blocks = TelegramFormatter.ToBlocks("## Ringkasan\n- item **satu**\n  - nested\n1. first\n---\n> quote\n- [x] done").ToList();

        Assert.Equal(["<b>Ringkasan</b>", "• item <b>satu</b>", "  • nested", "1. first", "──────────", "┃ quote", "☑ done"], blocks);
    }

    [Fact]
    public void Code_fences_are_escaped_verbatim_with_their_language()
    {
        var blocks = TelegramFormatter.ToBlocks("Before\n```csharp\nvar x = a < b && *p;\n```\nAfter").ToList();

        Assert.Equal(["Before", "<pre><code class=\"language-csharp\">var x = a &lt; b &amp;&amp; *p;</code></pre>", "After"], blocks);
    }

    [Fact]
    public void Tables_become_aligned_monospace_blocks()
    {
        var blocks = TelegramFormatter.ToBlocks("| File | Change |\n|---|---|\n| `a.cs` | **added** |").ToList();

        Assert.Equal(["<pre>File │ Change\n─────┼───────\na.cs │ added</pre>"], blocks);
    }

    [Fact]
    public void Blank_lines_are_collapsed()
    {
        Assert.Equal(["a", "", "b"], TelegramFormatter.ToBlocks("a\n\n\n\nb\n\n"));
    }

    [Fact]
    public void Long_text_is_split_into_chunks_within_the_limit_without_losing_lines()
    {
        var lines = Enumerable.Range(1, 200).Select(i => $"Baris {i:000} " + new string('x', 40)).ToList();

        var chunks = TelegramFormatter.ToChunks(string.Join("\n", lines), 1000);

        Assert.True(chunks.Count > 5);
        Assert.All(chunks, c => Assert.True(c.Length <= 1000));
        Assert.Equal(lines, chunks.SelectMany(c => c.Split('\n')));
    }

    [Fact]
    public void Long_code_is_split_into_balanced_pre_blocks()
    {
        var lines = Enumerable.Range(1, 300).Select(i => $"+ line {i} <tag> & more");

        var blocks = TelegramFormatter.CodeBlocks(lines, "diff", 1000).ToList();

        Assert.True(blocks.Count > 1);
        Assert.All(blocks, b =>
        {
            Assert.True(b.Length <= 1000);
            Assert.StartsWith("<pre><code class=\"language-diff\">", b);
            Assert.EndsWith("</code></pre>", b);
        });
    }

    [Fact]
    public void Plain_text_fallback_strips_tags_and_spells_out_links()
    {
        Assert.Equal("a & b <c> docs (https://x.dev/?a=1&b=2)",
            TelegramFormatter.ToPlainText("<b>a &amp; b</b> &lt;c&gt; <a href=\"https://x.dev/?a=1&amp;b=2\">docs</a>"));
    }

    [Fact]
    public void Truncate_adds_an_ellipsis_only_when_needed()
    {
        Assert.Equal("abc", TelegramFormatter.Truncate("abc", 5));
        Assert.Equal("abcd…", TelegramFormatter.Truncate("abcdefgh", 5));
    }
}
