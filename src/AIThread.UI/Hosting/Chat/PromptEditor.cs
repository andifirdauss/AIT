using System.Runtime.Versioning;
using System.Text;
using AIThread.Common.Localization;

namespace AIThread.UI.Hosting.Chat;

// What the developer did at the prompt: sent Text, or left the chat (Ctrl+C / Ctrl+D on an empty prompt).
public sealed record PromptInput(string? Text, bool Exit);

// The multi-line prompt of aithread chat. Enter sends; Shift+Enter, Alt+Enter, Ctrl+Enter, Ctrl+J or a trailing "\"
// start a new line (a paste whose lines arrive as Enter keys stays one prompt). Ctrl+V/Alt+V attach the clipboard
// image, and Ctrl+V pastes the clipboard's text when it holds no image (when the terminal passes Ctrl+V on instead of
// pasting itself). "@path" followed by a space or Enter attaches a file, and so does a paste or a file dragged onto the
// terminal that is nothing but image paths; all insert "[Image #N]" at the cursor, and a marker is deleted as a whole.
// A long paste (PastedTexts) shows as "[Paste #N, +L lines]", also deleted as a whole and expanded when sent.
// ↑/↓ move between lines, or through the history on the first/last line.
//
// While the prompt is a single "/word", the matching chat commands are listed below it with their description:
// ↑/↓ choose, Tab completes, Enter runs the chosen one, Esc closes the list until the text changes.
//
// The input is drawn below the conversation and redrawn as a whole after every key: the lines (wrapped by hand, so the
// width never depends on the terminal's own wrapping), the attachments and one message or hint line.
[SupportedOSPlatform("windows")]
public sealed class PromptEditor(ImageAttachments attachments)
{
    private const string FirstPrefix = "› ";
    private const string NextPrefix = "  ";
    private const int MenuRows = 8;

    private readonly List<string> _history = [];
    private StringBuilder _text = new();
    private int _cursor;
    private int _historyIndex;
    private string? _draft;
    private string? _message;
    private bool _messageIsError;
    private int _top;
    private int _rows;
    private int _menuIndex;
    private bool _menuClosed;
    private int? _pasteStart;
    private readonly PastedTexts _pastes = new();

    public ImageAttachments Attachments => attachments;

    // The prompt as it is sent: paste markers replaced by their text.
    public string ExpandPastes(string text) => _pastes.Expand(text);

    public PromptInput Read()
    {
        _text = new StringBuilder();
        _cursor = 0;
        _historyIndex = _history.Count;
        _draft = null;
        _message = null;
        _top = Console.CursorTop;
        _rows = 0;
        _menuIndex = 0;
        _menuClosed = false;

        var treatControlC = Console.TreatControlCAsInput;
        Console.TreatControlCAsInput = true;
        try
        {
            Render();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                _message = null;
                var before = _text.ToString();
                // A paste, or a file dragged onto the terminal, arrives as a burst of keys: where it started is kept,
                // and when the burst ends its text becomes image attachments or, when long, a paste marker.
                if (_pasteStart is null && Console.KeyAvailable) _pasteStart = _cursor;
                var result = Handle(key);
                if (_pasteStart is { } pasteStart && !Console.KeyAvailable)
                {
                    _pasteStart = null;
                    if (result is null) SettlePaste(pasteStart, _cursor);
                }
                // Typing opens the command list again at its first entry; ↑/↓ through the history leave it as History set it.
                if (key.Key is not (ConsoleKey.UpArrow or ConsoleKey.DownArrow) && _text.ToString() != before)
                {
                    _menuIndex = 0;
                    _menuClosed = false;
                }
                if (result is not null)
                {
                    Finish();
                    if (result.Text is { Length: > 0 } sent && (_history.Count == 0 || _history[^1] != sent)) _history.Add(sent);
                    return result;
                }
                // Drawn once a burst of keys (a paste) is through: redrawing after each of its keys is slow and flickers.
                if (!Console.KeyAvailable) Render();
            }
        }
        finally
        {
            Console.TreatControlCAsInput = treatControlC;
        }
    }

    private PromptInput? Handle(ConsoleKeyInfo key)
    {
        var control = (key.Modifiers & ConsoleModifiers.Control) != 0;
        var alt = (key.Modifiers & ConsoleModifiers.Alt) != 0;
        var shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;
        var menu = Menu();

        // Ctrl+V a terminal passes on may arrive as its control character (0x16) rather than as V with Control.
        if (key.KeyChar == '\u0016')
        {
            Paste(text: true);
            return null;
        }

        switch (key.Key)
        {
            case ConsoleKey.UpArrow when menu.Count > 0:
                _menuIndex = (_menuIndex - 1 + menu.Count) % menu.Count;
                return null;
            case ConsoleKey.DownArrow when menu.Count > 0:
                _menuIndex = (_menuIndex + 1) % menu.Count;
                return null;
            case ConsoleKey.Tab when menu.Count > 0:
                _text = new StringBuilder(SlashCommands.Complete(menu[_menuIndex]));
                _cursor = _text.Length;
                return null;
            case ConsoleKey.Escape when menu.Count > 0:
                _menuClosed = true;
                return null;
            case ConsoleKey.Enter when menu.Count > 0 && !shift && !alt && !control && !Console.KeyAvailable:
                return new PromptInput(menu[_menuIndex].Name, Exit: false);
            case ConsoleKey.C when control:
                if (_text.Length == 0) return new PromptInput(null, Exit: true);
                Clear();
                return null;
            case ConsoleKey.D when control:
                if (_text.Length == 0) return new PromptInput(null, Exit: true);
                return null;
            case ConsoleKey.V when control || alt:
                Paste(text: control && !alt);
                return null;
            case ConsoleKey.J when control:
                Insert("\n");
                return null;
            case ConsoleKey.Enter when shift || alt || control:
                Insert("\n");
                return null;
            case ConsoleKey.Enter:
                // A paste: its lines come as Enter keys followed at once by more input.
                if (Console.KeyAvailable)
                {
                    Insert("\n");
                    return null;
                }
                if (_cursor > 0 && _text[_cursor - 1] == '\\')
                {
                    _text.Remove(_cursor - 1, 1);
                    _cursor--;
                    Insert("\n");
                    return null;
                }
                AttachPathBeforeCursor();
                var text = _text.ToString();
                return text.Trim().Length == 0 ? null : new PromptInput(text, Exit: false);
            case ConsoleKey.Escape:
                Clear();
                return null;
            case ConsoleKey.Backspace:
                if (_cursor == 0) return null;
                if ((ImageAttachments.MarkerEndingAt(_text.ToString(), _cursor) ?? PastedTexts.MarkerEndingAt(_text.ToString(), _cursor)) is { } before)
                {
                    _text.Remove(before.Start, before.Length);
                    _cursor = before.Start;
                }
                else _text.Remove(--_cursor, 1);
                return null;
            case ConsoleKey.Delete:
                if (_cursor >= _text.Length) return null;
                if ((ImageAttachments.MarkerStartingAt(_text.ToString(), _cursor) ?? PastedTexts.MarkerStartingAt(_text.ToString(), _cursor)) is { } after)
                {
                    _text.Remove(after.Start, after.Length);
                    _cursor = Math.Min(_cursor, after.Start);
                }
                else _text.Remove(_cursor, 1);
                return null;
            case ConsoleKey.LeftArrow:
                _cursor = control ? WordStart(_cursor) : Math.Max(0, _cursor - 1);
                return null;
            case ConsoleKey.RightArrow:
                _cursor = control ? WordEnd(_cursor) : Math.Min(_text.Length, _cursor + 1);
                return null;
            case ConsoleKey.Home:
                _cursor = LineStart(_cursor);
                return null;
            case ConsoleKey.End:
                _cursor = LineEnd(_cursor);
                return null;
            case ConsoleKey.UpArrow:
                if (LineStart(_cursor) > 0) MoveLine(-1);
                else History(-1);
                return null;
            case ConsoleKey.DownArrow:
                if (LineEnd(_cursor) < _text.Length) MoveLine(1);
                else History(1);
                return null;
            case ConsoleKey.Tab:
                Insert("    ");
                return null;
        }

        if (key.KeyChar == '\n')
        {
            Insert("\n");
            return null;
        }
        if (key.KeyChar < ' ' || control && !alt) return null;

        Insert(key.KeyChar.ToString());
        if (key.KeyChar == ' ' && !Console.KeyAvailable) AttachPathBeforeCursor(spaceTyped: true);
        return null;
    }

    private void Insert(string value)
    {
        _text.Insert(_cursor, value);
        _cursor += value.Length;
    }

    private void Clear()
    {
        _text.Clear();
        _cursor = 0;
    }

    // The clipboard image as an attachment; text: without an image, its text is pasted (Ctrl+V), and a text that is
    // only image paths becomes attachments too.
    private void Paste(bool text)
    {
        var content = ClipboardImage.Read();
        if (content is null)
        {
            if (text && ClipboardImage.ReadText() is { Length: > 0 } pasted)
            {
                var start = _cursor;
                Insert(pasted.ReplaceLineEndings("\n").Replace("\t", "    "));
                SettlePaste(start, _cursor);
                return;
            }
            Show(text ? Lang.T("Clipboard kosong.", "The clipboard is empty.") : Lang.T("Clipboard tidak berisi gambar.", "The clipboard holds no image."), error: true);
            return;
        }

        var number = ImageAttachments.NextNumber(_text.ToString());
        var image = content.Png is { } png
            ? ImageAttachments.FromBytes("clipboard.png", png, number, out var problem)
            : ImageAttachments.Load(content.FilePath!, Environment.CurrentDirectory, number, out problem);
        AttachAt(image, problem, _cursor, 0);
    }

    // "@path" right before the cursor (a space just typed after it when spaceTyped) becomes an attachment.
    private void AttachPathBeforeCursor(bool spaceTyped = false)
    {
        var end = spaceTyped ? _cursor - 1 : _cursor;
        if (ImageAttachments.PathTokenEndingAt(_text.ToString(), end) is not { } token) return;

        var image = ImageAttachments.Load(token.Path, Environment.CurrentDirectory, ImageAttachments.NextNumber(_text.ToString()), out var problem);
        AttachAt(image, problem, token.Start, token.Length);
    }

    // The text from start to end (a paste or a dropped file): image paths become attachments, a long text becomes a
    // paste marker (PastedTexts), anything else stays as typed.
    private void SettlePaste(int start, int end)
    {
        if (start < 0 || end > _text.Length || end <= start) return;
        var raw = _text.ToString(start, end - start);
        if (AttachPasted(raw, start)) return;
        if (!PastedTexts.IsLarge(raw)) return;

        var marker = _pastes.Add(raw);
        _text.Remove(start, raw.Length);
        _text.Insert(start, marker);
        _cursor = start + marker.Length;
    }

    // Replaces the pasted raw text at start with attachments when it is only image paths; every image is checked first,
    // so a refused one leaves the text as it is. False when the text is not image paths.
    private bool AttachPasted(string raw, int start)
    {
        if (ImageAttachments.PastedImagePaths(raw, Environment.CurrentDirectory) is not { } paths) return false;

        var text = _text.ToString();
        if (attachments.Active(text).Count + paths.Count > Common.Images.PromptImages.MaxCount)
        {
            Show(Common.Images.PromptImages.TooMany, error: true);
            return true;
        }
        var images = new List<ChatImage>();
        foreach (var path in paths)
        {
            if (ImageAttachments.Load(path, Environment.CurrentDirectory, ImageAttachments.NextNumber(text) + images.Count, out var problem) is not { } image)
            {
                Show(problem!, error: true);
                return true;
            }
            images.Add(image);
        }

        var lead = raw.Length - raw.TrimStart().Length;
        _text.Remove(start + lead, raw.Trim().Length);
        _cursor = start + lead;
        for (var i = 0; i < images.Count; i++)
        {
            if (i > 0) Insert(" ");
            AttachAt(images[i], null, _cursor, 0);
        }
        return true;
    }

    // Replaces length characters at start with the marker of the image; a refused image leaves the text as it is.
    private void AttachAt(ChatImage? image, string? problem, int start, int length)
    {
        if (image is null)
        {
            Show(problem!, error: true);
            return;
        }

        var text = _text.ToString();
        var withoutToken = text.Remove(start, length);
        if (attachments.Add(image, withoutToken, out problem) is not { } marker)
        {
            Show(problem!, error: true);
            return;
        }

        _text.Remove(start, length);
        _text.Insert(start, marker);
        _cursor = _cursor >= start + length ? _cursor - length + marker.Length : start + marker.Length;
    }

    private void Show(string message, bool error)
    {
        _message = message;
        _messageIsError = error;
    }

    private int LineStart(int position)
    {
        var text = _text.ToString();
        return position == 0 ? 0 : text.LastIndexOf('\n', position - 1) + 1;
    }

    private int LineEnd(int position)
    {
        var next = _text.ToString().IndexOf('\n', position);
        return next < 0 ? _text.Length : next;
    }

    private void MoveLine(int direction)
    {
        var start = LineStart(_cursor);
        var column = _cursor - start;
        if (direction < 0)
        {
            var previousStart = LineStart(start - 1);
            _cursor = Math.Min(previousStart + column, start - 1);
        }
        else
        {
            var nextStart = LineEnd(_cursor) + 1;
            _cursor = Math.Min(nextStart + column, LineEnd(nextStart));
        }
    }

    private void History(int direction)
    {
        if (_history.Count == 0) return;
        var index = Math.Clamp(_historyIndex + direction, 0, _history.Count);
        if (index == _historyIndex) return;

        if (_historyIndex == _history.Count) _draft = _text.ToString();
        _historyIndex = index;
        var value = index == _history.Count ? _draft ?? string.Empty : _history[index];
        _text = new StringBuilder(value);
        _cursor = value.Length;
        // A "/command" from the history keeps ↑/↓ on the history instead of opening the command list.
        _menuClosed = true;
    }

    private IReadOnlyList<SlashCommand> Menu() => _menuClosed ? [] : SlashCommands.Matching(_text.ToString());

    private int WordStart(int position)
    {
        while (position > 0 && char.IsWhiteSpace(_text[position - 1])) position--;
        while (position > 0 && !char.IsWhiteSpace(_text[position - 1])) position--;
        return position;
    }

    private int WordEnd(int position)
    {
        while (position < _text.Length && char.IsWhiteSpace(_text[position])) position++;
        while (position < _text.Length && !char.IsWhiteSpace(_text[position])) position++;
        return position;
    }

    // final: the prompt as it stays in the conversation, without the command list and the hint line.
    private void Render(bool final = false)
    {
        var width = Math.Max(20, Console.BufferWidth - 1);
        var text = _text.ToString();
        var rows = new List<(string Text, ConsoleColor? Color)>();
        int cursorRow = 0, cursorColumn = 0;

        var lineStart = 0;
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var full = (i == 0 ? FirstPrefix : NextPrefix) + lines[i];
            var prefix = i == 0 ? FirstPrefix.Length : NextPrefix.Length;
            if (_cursor >= lineStart && _cursor <= lineStart + lines[i].Length)
            {
                var offset = prefix + _cursor - lineStart;
                cursorRow = rows.Count + offset / width;
                cursorColumn = offset % width;
            }

            // One extra row when the line fills its last row exactly, so the cursor after it has a place.
            var count = full.Length / width + 1;
            for (var r = 0; r < count; r++)
            {
                var from = r * width;
                rows.Add((from < full.Length ? full.Substring(from, Math.Min(width, full.Length - from)) : string.Empty, null));
            }
            lineStart += lines[i].Length + 1;
        }

        IReadOnlyList<SlashCommand> menu = final ? [] : Menu();
        if (menu.Count > 0)
        {
            // A window of MenuRows entries that keeps the chosen one in view.
            var column = menu.Max(c => c.Usage.Length) + 3;
            var first = Math.Clamp(_menuIndex - MenuRows + 1, 0, Math.Max(0, menu.Count - MenuRows));
            for (var i = first; i < Math.Min(menu.Count, first + MenuRows); i++)
            {
                var chosen = i == _menuIndex;
                rows.Add((Fit((chosen ? "  › " : "    ") + menu[i].Usage.PadRight(column) + menu[i].Summary, width), chosen ? ConsoleColor.Cyan : ConsoleColor.Gray));
            }
        }

        foreach (var (number, image) in attachments.Active(text))
            rows.Add((Fit($"  • {ImageAttachments.Marker(number)} {image.Name} · {Common.Images.PromptImages.Size(image.Data.Length)}", width), ConsoleColor.DarkCyan));

        if (!final)
            rows.Add(_message is not null
                ? (Fit("  " + _message, width), _messageIsError ? ConsoleColor.Yellow : ConsoleColor.DarkGray)
                : menu.Count > 0
                    ? (Fit(Lang.T("  ↑/↓ pilih · Tab lengkapi · Enter jalankan · Esc tutup", "  ↑/↓ choose · Tab complete · Enter run · Esc close"), width), ConsoleColor.DarkGray)
                    : (Fit(Lang.T("  Enter kirim · Shift+Enter baris baru · Alt+V gambar clipboard · @file.png · / perintah", "  Enter send · Shift+Enter new line · Alt+V clipboard image · @file.png · / commands"), width), ConsoleColor.DarkGray));

        Console.CursorVisible = false;
        try
        {
            // Wipe what the previous draw left, then draw from the same top row.
            var blank = new string(' ', width);
            for (var r = 0; r < _rows && _top + r < Console.BufferHeight; r++)
            {
                Console.SetCursorPosition(0, _top + r);
                Console.Write(blank);
            }
            Console.SetCursorPosition(0, Math.Min(_top, Console.BufferHeight - 1));

            var previous = Console.ForegroundColor;
            for (var r = 0; r < rows.Count; r++)
            {
                if (r > 0) Console.Write('\n');
                if (rows[r].Color is { } color) Console.ForegroundColor = color;
                Console.Write(rows[r].Text);
                Console.ForegroundColor = previous;
            }

            // Drawing at the bottom of the buffer scrolls it; the top row is where the first row ended up.
            _top = Math.Max(0, Console.CursorTop - (rows.Count - 1));
            _rows = rows.Count;
            Console.SetCursorPosition(Math.Min(cursorColumn, width), Math.Min(_top + cursorRow, Console.BufferHeight - 1));
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }

    // The final draw, then the cursor on the line below the prompt.
    private void Finish()
    {
        _message = null;
        Render(final: true);
        Console.SetCursorPosition(0, Math.Min(_top + _rows - 1, Console.BufferHeight - 1));
        Console.WriteLine();
    }

    private static string Fit(string text, int width) => text.Length <= width ? text : text[..(width - 1)] + "…";
}
