using System.Text;
using System.Text.RegularExpressions;
using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

// What the client (the coding CLI) sees: the thread anchor, then the progress as a vertical stepper inside a ````text
// block (phases, their steps and a closing line per phase), then a separator and the final answer.
//
//   🧠 Planner · model
//   │  ├─ read_file       src/A.cs
//   │  └─ selesai dalam 3.1s
//   │
//   📋 Rencana · 2 tugas
//
// The client only appends text, so a line is never changed after it was written. Lines are wrapped at Width columns
// with the tree prefix repeated; backticks are dropped inside the block so it can never be closed early. Progress
// after the final answer started is not shown. Also keeps the full transcript for non-streaming callers. Observer
// failures (client gone) never break a thread.
public sealed partial class ClientProgress(IThreadObserver observer, bool showProgress)
{
    public const int Width = 110;

    private const string Fence = "````";
    private const string Rail = "│  ";
    private const string Branch = "│  ├─ ";
    private const string LastBranch = "│  └─ ";
    private const string Under = "│  │  ";
    private const string UnderLast = "│     ";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly StringBuilder _transcript = new();
    private bool _anyOutput;
    private bool _fenceOpen;
    private bool _finalStarted;
    private int _phases;

    public string Transcript
    {
        get
        {
            lock (_transcript) return _transcript.ToString();
        }
    }

    // Written even when progress is hidden: the context keeper finds earlier threads through this line (ThreadAnchor).
    public Task AnchorAsync(string line, CancellationToken cancellationToken) =>
        WriteAsync(_ => line + "\n", isFinal: false, cancellationToken);

    // "🧠 Planner · model", with an empty rail line before every phase but the first.
    public Task PhaseAsync(string icon, string title, CancellationToken cancellationToken) =>
        ProgressAsync(phases => (phases > 0 ? "│\n" : string.Empty) + Wrap($"{icon} {title}", string.Empty, Rail), cancellationToken, newPhase: true);

    // Free text under the phase title (the plan summary).
    public Task TextAsync(string text, CancellationToken cancellationToken) =>
        ProgressAsync(_ => Wrap(text, Rail, Rail), cancellationToken);

    // One entry of the phase; last closes it with └─. Wrapped lines start hang columns after the branch.
    public Task StepAsync(string text, CancellationToken cancellationToken, bool last = false, int hang = 0) =>
        ProgressAsync(_ => Wrap(text, last ? LastBranch : Branch, (last ? UnderLast : Under) + new string(' ', hang)), cancellationToken);

    // A line under the entry written just before; underLast when that entry closed the phase.
    public Task DetailAsync(string text, CancellationToken cancellationToken, int indent = 3, bool underLast = false, int hang = 0)
    {
        var prefix = (underLast ? UnderLast : Under) + new string(' ', indent);
        return ProgressAsync(_ => Wrap(text, prefix, prefix + new string(' ', hang)), cancellationToken);
    }

    public Task FinalAsync(string delta, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(delta) ? Task.CompletedTask : WriteAsync(_ => delta, isFinal: true, cancellationToken);

    // Closes the progress block when the thread ends without a final answer (cancelled), so the client's
    // Markdown is not left inside an open block.
    public async Task CloseAsync()
    {
        await _lock.WaitAsync(CancellationToken.None);
        try
        {
            if (_fenceOpen && !_finalStarted)
            {
                _fenceOpen = false;
                await EmitAsync(Fence + "\n", isFinal: false, CancellationToken.None);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    // Display columns of text in a monospace terminal: emoji count two columns, variation selectors none.
    public static int DisplayWidth(string text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes()) width += RuneWidth(rune.Value);
        return width;
    }

    // Pads to a number of display columns (string.PadRight counts UTF-16 units, which breaks on emoji).
    public static string Pad(string text, int columns) => text + new string(' ', Math.Max(0, columns - DisplayWidth(text)));

    public static string PadLeft(string text, int columns) => new string(' ', Math.Max(0, columns - DisplayWidth(text))) + text;

    // "[↑ 12.2k ↓  424  0%   3.4s]": prompt and completion tokens of one model turn, the cache hit share of the prompt
    // (cached tokens are part of the prompt) and how long the model took to answer (without the wait for a slot). Every
    // slot is left-padded to fixed columns (↑ 6, ↓ 5, % 3, time 6) so every badge is BadgeWidth columns wide; the share
    // is floored and capped at 99 to keep that width.
    public static string TokenBadge(int prompt, int completion, int cached, TimeSpan elapsed)
    {
        var pct = prompt > 0 ? Math.Min(99, (int)((long)cached * 100 / prompt)) : 0;
        return "[↑" + PadLeft(FinalReport.Tokens(prompt), 6) + " ↓" + PadLeft(FinalReport.Tokens(completion), 5)
            + " " + PadLeft(pct + "%", 3) + " " + PadLeft(FinalReport.Duration(elapsed), 6) + "]";
    }

    public static int BadgeWidth => DisplayWidth(TokenBadge(0, 0, 0, TimeSpan.Zero));

    // The column between a tool name and its arguments: ". " + the badge, or blanks of the same width so the arguments
    // of every tool line start at the same column.
    public static string BadgeSlot(string? badge) => ". " + (badge ?? new string(' ', BadgeWidth)) + "  ";

    public static int SlotWidth => DisplayWidth(BadgeSlot(null));

    // Shortens a long path to its first folder and as many trailing segments as fit: "Web/…/mkppdf/script.js".
    public static string CompactPath(string path, int max)
    {
        if (path.Length <= max) return path;
        var parts = path.Replace('\\', '/').Split('/');
        if (parts.Length < 3) return "…" + path[^(max - 1)..];

        var head = parts[0] + "/…/";
        var tail = parts[^1];
        for (var i = parts.Length - 2; i > 0; i--)
        {
            var longer = parts[i] + "/" + tail;
            if (head.Length + longer.Length > max) break;
            tail = longer;
        }
        var compact = head + tail;
        return compact.Length <= max ? compact : "…" + tail[^Math.Min(tail.Length, max - 1)..];
    }

    // Splits text into lines of at most Width columns: the first starts with firstPrefix, the others with nextPrefix.
    // Spaces between words are kept (column padding); a wrapped line drops the spaces it would start with. A word moves
    // to a new line only when it fits there; a word too long for a fresh line is cut in place instead of moved.
    // Backticks are removed: they would show literally inside the block.
    public static string Wrap(string text, string firstPrefix, string nextPrefix)
    {
        var builder = new StringBuilder();
        var prefix = firstPrefix;
        var clean = text.Replace("`", string.Empty).Replace("\r", string.Empty).Replace('\t', ' ');
        foreach (var paragraph in clean.Split('\n'))
        {
            var line = new StringBuilder(prefix);
            var lineWidth = DisplayWidth(prefix);
            var lineHasWord = false;
            foreach (Match token in Token().Matches(paragraph))
            {
                var gap = token.Groups[1].Value;
                var word = token.Groups[2].Value;
                if (lineHasWord && lineWidth + gap.Length + DisplayWidth(word) > Width && DisplayWidth(nextPrefix) + DisplayWidth(word) <= Width)
                {
                    builder.Append(line).Append('\n');
                    prefix = nextPrefix;
                    line = new StringBuilder(prefix);
                    lineWidth = DisplayWidth(prefix);
                    lineHasWord = false;
                    gap = string.Empty;
                }

                var rest = word;
                line.Append(gap);
                lineWidth += gap.Length;
                while (lineWidth + DisplayWidth(rest) > Width && Width - lineWidth > 1)
                {
                    var (head, tail) = Cut(rest, Width - lineWidth);
                    builder.Append(line).Append(head).Append('\n');
                    prefix = nextPrefix;
                    line = new StringBuilder(prefix);
                    lineWidth = DisplayWidth(prefix);
                    rest = tail;
                }
                line.Append(rest);
                lineWidth += DisplayWidth(rest);
                lineHasWord = true;
            }
            builder.Append(line.ToString().TrimEnd()).Append('\n');
            prefix = nextPrefix;
        }
        return builder.ToString();
    }

    private static (string Head, string Tail) Cut(string word, int columns)
    {
        var width = 0;
        var index = 0;
        foreach (var rune in word.EnumerateRunes())
        {
            var runeWidth = RuneWidth(rune.Value);
            if (width + runeWidth > columns) break;
            width += runeWidth;
            index += rune.Utf16SequenceLength;
        }
        return (word[..index], word[index..]);
    }

    private static int RuneWidth(int value) => value switch
    {
        0xFE0F or 0xFE0E or 0x200D => 0,
        >= 0x1F000 => 2,
        0x2705 or 0x274C or 0x2757 or 0x23E9 or 0x23F3 or 0x231B => 2,
        _ => 1
    };

    // Spaces before a word, then the word.
    [GeneratedRegex(@"( *)(\S+)")]
    private static partial Regex Token();

    private Task ProgressAsync(Func<int, string> render, CancellationToken cancellationToken, bool newPhase = false) =>
        showProgress ? WriteAsync(render, isFinal: false, cancellationToken, isProgress: true, newPhase) : Task.CompletedTask;

    private async Task WriteAsync(Func<int, string> render, bool isFinal, CancellationToken cancellationToken, bool isProgress = false, bool newPhase = false)
    {
        await _lock.WaitAsync(CancellationToken.None);
        try
        {
            if (isFinal)
            {
                if (!_finalStarted)
                {
                    _finalStarted = true;
                    var opening = new StringBuilder();
                    if (_fenceOpen) opening.Append(Fence).Append('\n');
                    _fenceOpen = false;
                    if (_anyOutput) opening.Append("\n---\n\n");
                    if (opening.Length > 0) await EmitAsync(opening.ToString(), isFinal: true, cancellationToken);
                }
                await EmitAsync(render(_phases), isFinal: true, cancellationToken);
                return;
            }

            // The answer is being written; a late progress line would land inside it.
            if (_finalStarted) return;
            _anyOutput = true;
            if (!isProgress)
            {
                await EmitAsync(render(_phases), isFinal: false, cancellationToken);
                return;
            }

            var text = render(_phases);
            if (newPhase) _phases++;
            if (!_fenceOpen)
            {
                _fenceOpen = true;
                text = Fence + "text\n" + text;
            }
            await EmitAsync(text, isFinal: false, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EmitAsync(string text, bool isFinal, CancellationToken cancellationToken)
    {
        lock (_transcript) _transcript.Append(text);
        try
        {
            if (isFinal) await observer.OnFinalDeltaAsync(text, cancellationToken);
            else await observer.OnProgressAsync(text, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // The client stopped listening; the thread keeps its own record.
        }
    }
}
