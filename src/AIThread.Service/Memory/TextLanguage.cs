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

    // Short words developers type in a request, on top of the lists above (requests are often a few words long).
    private static readonly HashSet<string> IndonesianPromptWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "tolong", "buat", "buatkan", "bikin", "tambahkan", "ganti", "hapus", "hapuskan", "perbaiki", "cek", "cari", "kenapa",
        "bagaimana", "gimana", "apa", "apakah", "dong", "saya", "aku", "kamu", "gak", "ga", "nggak", "enggak", "mau", "jadi",
        "kalau", "kalo", "sama", "biar", "supaya", "gitu", "aja", "sih", "deh", "yg", "dgn", "utk", "jalankan", "ulangi",
        "coba", "lagi", "skenario", "lanjutan", "setuju", "sekarang", "semua", "kok", "nih", "dulu", "terus", "lalu", "sudah",
        "belum", "pakai", "pakailah", "tampilkan", "lihat", "jelaskan", "ini", "itu", "nya", "kan"
    };

    private static readonly HashSet<string> EnglishPromptWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "please", "make", "add", "change", "remove", "delete", "fix", "check", "find", "why", "how", "what", "does", "do",
        "i", "you", "my", "me", "run", "retry", "again", "create", "scenario", "follow", "agree", "now", "all", "it", "an",
        "show", "explain", "use", "let", "there", "should", "would", "could", "it's", "don't", "doesn't", "we", "our"
    };

    // The language a developer writes a request in, for AIT's own output to that chat: the words of the prompt, else of
    // the earlier user turns of the conversation ("[user] …" parts), else null (the UI language applies). Pasted code,
    // logs and attached documents do not count. Indonesian words weigh double: Indonesian requests are full of English
    // technical words, English ones rarely hold Indonesian words.
    public static string? OfPrompt(string? prompt, string? conversation = null)
    {
        if (Score(prompt) is { } fromPrompt) return fromPrompt;
        if (string.IsNullOrWhiteSpace(conversation)) return null;
        var userTurns = UserTurn().Matches(conversation).Select(m => m.Groups[1].Value).Reverse().Take(5);
        return Score(string.Join('\n', userTurns));
    }

    // OfPrompt as a Lang code for Lang.Use: the UI language when the words do not tell.
    public static string LangOfPrompt(string? prompt, string? conversation = null) => OfPrompt(prompt, conversation) switch
    {
        English => Lang.English,
        Indonesian => Lang.Indonesian,
        _ => Lang.Current
    };

    private static string? Score(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = Markup().Replace(Fenced().Replace(text, " "), " ");
        int indonesian = 0, english = 0;
        foreach (var line in text.Split('\n'))
        {
            if (CodeLine().IsMatch(line)) continue;
            foreach (Match word in Words().Matches(Prose(line)))
            {
                if (IndonesianWords.Contains(word.Value) || IndonesianPromptWords.Contains(word.Value)) indonesian += 2;
                else if (EnglishWords.Contains(word.Value) || EnglishPromptWords.Contains(word.Value)) english++;
            }
        }
        return indonesian > english ? Indonesian : english > indonesian ? English : null;
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

    [GeneratedRegex(@"```.*?(```|$)", RegexOptions.Singleline)]
    private static partial Regex Fenced();

    // Attached documents, system reminders and the like: <tag …>…</tag>.
    [GeneratedRegex(@"<([A-Za-z][\w-]*)[^>]*>.*?</\1>", RegexOptions.Singleline)]
    private static partial Regex Markup();

    // A line of pasted code or a log: braces, a statement end, a lambda, a stack frame, an exception.
    [GeneratedRegex(@"[{};]|=>|^\s*at\s|Exception\b|^\s*(//|#|\$|>)")]
    private static partial Regex CodeLine();

    [GeneratedRegex(@"(?:^|\n\n)\[user\] (.*?)(?=\n\n\[(?:user|assistant)\] |$)", RegexOptions.Singleline)]
    private static partial Regex UserTurn();
}
