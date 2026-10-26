using System.Globalization;

namespace AIThread.Common.Localization;

// The language of the text AIT shows its user: the web UI, the output to the CLI, activity log, validation and API
// messages, the exe's menu. Every such text is written in place as a pair, Lang.T("Indonesian", "English"), so both
// stay next to where they are used. Prompts to models are English and never go through here.
// Current is the machine's setting (Setting → Bahasa, SystemSetting.UiLanguage); Use() overrides it for the current
// async flow only (tests, or work done on behalf of a specific chat).
public static class Lang
{
    public const string Indonesian = "id";
    public const string English = "en";

    private static readonly CultureInfo IndonesianCulture = CultureInfo.GetCultureInfo("id-ID");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en-US");
    private static readonly AsyncLocal<string?> Scoped = new();
    private static volatile string _current = Indonesian;

    // Raised after the machine's language changed (not for Use scopes).
    public static event Action<string>? Changed;

    public static string Current => Scoped.Value ?? _current;

    public static bool IsEnglish => Current == English;

    public static CultureInfo Culture => IsEnglish ? EnglishCulture : IndonesianCulture;

    public static string T(string indonesian, string english) => IsEnglish ? english : indonesian;

    // "en", "EN", "en-US", "English" → en; anything else → id (the default).
    public static string Normalize(string? code)
    {
        var value = code?.Trim().ToLowerInvariant();
        return value is "en" or "english" || value?.StartsWith("en-", StringComparison.Ordinal) == true ? English : Indonesian;
    }

    public static void Set(string? code)
    {
        var next = Normalize(code);
        if (next == _current) return;
        _current = next;
        Changed?.Invoke(next);
    }

    // The language for the rest of the current async flow; disposing restores the previous one.
    public static IDisposable Use(string? code)
    {
        var previous = Scoped.Value;
        Scoped.Value = Normalize(code);
        return new Restore(previous);
    }

    private sealed class Restore(string? previous) : IDisposable
    {
        public void Dispose() => Scoped.Value = previous;
    }
}
