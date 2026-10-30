using AIThread.Common.Chat;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;

namespace AIThread.UI.Hosting.Chat;

public sealed record SessionResolution(ChatSessionDto? Session, string? Error);

// Finding the chat session to resume: -r/--resume and /resume take the id the chat shows (its first hex digits, any
// length) or the whole key; -c/--continue takes the most recent one.
public static class ChatSessions
{
    public static SessionResolution Find(IReadOnlyList<ChatSessionDto> sessions, string wanted)
    {
        var text = wanted.Trim().ToLowerInvariant();
        if (text.StartsWith(ChatSessionKey.Prefix, StringComparison.Ordinal)) text = text[ChatSessionKey.Prefix.Length..];
        if (text.Length == 0) return new SessionResolution(null, Lang.T("Id sesi kosong.", "The session id is empty."));

        var matches = sessions.Where(s => s.SessionId[ChatSessionKey.Prefix.Length..].StartsWith(text, StringComparison.Ordinal)).ToList();
        return matches.Count switch
        {
            1 => new SessionResolution(matches[0], null),
            0 => new SessionResolution(null, Lang.T($"Sesi '{wanted}' tidak ditemukan di project ini.", $"Session '{wanted}' was not found in this project.")),
            _ => new SessionResolution(null, Lang.T($"'{wanted}' cocok dengan {matches.Count} sesi; tulis id yang lebih panjang.",
                $"'{wanted}' matches {matches.Count} sessions; write a longer id."))
        };
    }

    // "a1b2c3d4 · Rubah registry ke AIT · 7 prompt · 2 jam lalu", as the resume list shows a session.
    public static string Describe(ChatSessionDto session, DateTime now) =>
        $"{ChatSessionKey.Short(session.SessionId)} · {session.Title} · " +
        Lang.T($"{session.Turns} prompt · {Ago(session.LastAt, now)}", $"{session.Turns} prompt{(session.Turns == 1 ? "" : "s")} · {Ago(session.LastAt, now)}");

    public static string Ago(DateTime at, DateTime now)
    {
        var age = now - at;
        if (age < TimeSpan.FromMinutes(1)) return Lang.T("baru saja", "just now");
        if (age < TimeSpan.FromHours(1)) return Lang.T($"{(int)age.TotalMinutes} menit lalu", $"{(int)age.TotalMinutes} min ago");
        if (age < TimeSpan.FromDays(1)) return Lang.T($"{(int)age.TotalHours} jam lalu", $"{(int)age.TotalHours} h ago");
        return Lang.T($"{(int)age.TotalDays} hari lalu", $"{(int)age.TotalDays} d ago");
    }
}
