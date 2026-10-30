namespace AIThread.UI.Hosting.Chat;

// A list of choices drawn below the conversation and redrawn in place, one row per choice (cut to the width) plus a
// hint row; the chosen row is marked. The caller reads the keys and moves the mark.
public sealed class ChoiceMenu(IReadOnlyList<string> items, string hint)
{
    private int _top;
    private int _rows;

    public int Index { get; private set; }

    public void Move(int delta)
    {
        Index = (Index + delta + items.Count) % items.Count;
        Draw();
    }

    public void Draw()
    {
        var width = Width;
        var rows = items.Select((item, i) => (Fit((i == Index ? "  › " : "    ") + item, width), i == Index ? ConsoleColor.Cyan : ConsoleColor.Gray)).ToList();
        rows.Add((Fit("  " + hint, width), ConsoleColor.DarkGray));
        Write(rows);
    }

    // Leaves one row in place of the list: the answer given, or nothing when chosen is null; the cursor goes below it.
    public void Close(string? chosen)
    {
        Write(chosen is null ? [] : [(Fit("  › " + chosen, Width), ConsoleColor.Green)]);
        if (_rows > 0) Console.WriteLine();
    }

    public static string Fit(string text, int width) => text.Length <= width ? text : text[..(width - 1)] + "…";

    private static int Width => Math.Max(20, Console.BufferWidth - 1);

    private void Write(IReadOnlyList<(string Text, ConsoleColor Color)> rows)
    {
        if (_rows == 0) _top = Console.CursorTop;
        Console.CursorVisible = false;
        try
        {
            // Wipe what the previous draw left, then draw from the same top row.
            var blank = new string(' ', Width);
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
                Console.ForegroundColor = rows[r].Color;
                Console.Write(rows[r].Text);
                Console.ForegroundColor = previous;
            }

            // Drawing at the bottom of the buffer scrolls it; the top row is where the first row ended up.
            if (rows.Count > 0) _top = Math.Max(0, Console.CursorTop - (rows.Count - 1));
            _rows = rows.Count;
        }
        finally
        {
            Console.CursorVisible = true;
        }
    }
}
