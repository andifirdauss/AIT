using System.Text.RegularExpressions;
using AIThread.Common.Localization;

namespace AIThread.Service.Memory;

// The language memory text is written in, as far as AIT needs to know: Indonesian or English (the languages its users
// write in), or foreign when a cheap model drifted into another one (Vietnamese, Thai, …). Code in backticks is ignored.
public static partial class TextLanguage
{
    public const string Indonesian = "Indonesian";
    public const string English = "English";

    // The UI language as a memory language: the default when the words do not tell.
    public static string Ui => Lang.IsEnglish ? English : Indonesian;

    private static readonly HashSet<string> IndonesianWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "yang", "dan", "untuk", "dari", "dengan", "tidak", "wajib", "saat", "ini", "itu", "harus", "akan", "sudah", "belum",
        "bisa", "atau", "pada", "karena", "jika", "bila", "agar", "dalam", "oleh", "setelah", "sebelum", "tanpa", "lalu",
        "menjadi", "masih", "hanya", "juga", "di", "ke", "ada", "maks", "diubah", "ditambah", "dihapus", "baru", "ubah"
    };

    private static readonly HashSet<string> EnglishWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "from", "with", "not", "must", "when", "this", "that", "should", "will", "already", "can", "or",
        "on", "because", "if", "into", "after", "before", "without", "then", "becomes", "still", "only", "also", "is", "are",
        "be", "to", "of", "in", "added", "removed", "changed", "new"
    };

    // Indonesian or English when the words say so clearly, else null (too short, mixed, or foreign).
    public static string? Guess(IEnumerable<string?> texts)
    {
        int indonesian = 0, english = 0;
        foreach (var text in texts)
        {
            if (string.IsNullOrWhiteSpace(text) || IsForeign(text)) continue;
            foreach (Match word in Words().Matches(Prose(text)))
            {
                if (IndonesianWords.Contains(word.Value)) indonesian++;
                else if (EnglishWords.Contains(word.Value)) english++;
            }
        }
        if (indonesian + english < 3) return null;
        return indonesian >= english * 1.5 ? Indonesian : english >= indonesian * 1.5 ? English : null;
    }

    // Prose written in another script or in a language with its own letters (ă, ơ, ư, đ, ạ, …): more than a few of
    // its letters are outside ASCII. Accented names (José, Müller) stay well below that.
    public static bool IsForeign(string text)
    {
        int letters = 0, other = 0;
        foreach (var c in Prose(text))
        {
            if (!char.IsLetter(c)) continue;
            letters++;
            if (c > 0x7F) other++;
        }
        return letters >= 20 && other * 100 > letters * 4;
    }

    private static string Prose(string text) => Code().Replace(text, " ");

    [GeneratedRegex(@"`[^`\n]*`")]
    private static partial Regex Code();

    [GeneratedRegex(@"\p{L}+")]
    private static partial Regex Words();
}
