using System.Text.RegularExpressions;

namespace AIThread.UI.Hosting.Chat;

// Long pasted text of aithread chat, kept out of the prompt editor: a paste of more than MaxLines lines or MaxChars
// characters (the limits of oh-my-pi) shows as "[Paste #N, +L lines]" or "[Paste #N, C chars]", deleted as a whole,
// and is put back in its place when the prompt is sent. Pastes are kept for the whole chat, so a prompt recalled from
// the history still expands.
public sealed partial class PastedTexts
{
    public const int MaxLines = 10;
    public const int MaxChars = 1000;

    private readonly Dictionary<int, string> _pastes = [];
    private int _counter;

    public static bool IsLarge(string text) => text.Length > MaxChars || text.Split('\n').Length > MaxLines;

    // Keeps the text and returns its marker.
    public string Add(string text)
    {
        var id = ++_counter;
        _pastes[id] = text;
        var lines = text.Split('\n').Length;
        return lines > MaxLines ? $"[Paste #{id}, +{lines} lines]" : $"[Paste #{id}, {text.Length} chars]";
    }

    // The prompt with every known marker replaced by its text; a marker typed by hand stays as it is.
    public string Expand(string text) =>
        MarkerPattern().Replace(text, match => _pastes.TryGetValue(int.Parse(match.Groups[1].Value), out var pasted) ? pasted : match.Value);

    public static (int Start, int Length)? MarkerEndingAt(string text, int end) =>
        MarkerPattern().Matches(text).Where(m => m.Index < end && m.Index + m.Length >= end).Select(m => ((int, int)?)(m.Index, m.Length)).FirstOrDefault();

    public static (int Start, int Length)? MarkerStartingAt(string text, int start) =>
        MarkerPattern().Matches(text).Where(m => m.Index <= start && m.Index + m.Length > start).Select(m => ((int, int)?)(m.Index, m.Length)).FirstOrDefault();

    [GeneratedRegex(@"\[Paste #(\d+), (?:\+\d+ lines|\d+ chars)\]")]
    private static partial Regex MarkerPattern();
}
