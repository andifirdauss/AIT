using System.Text.RegularExpressions;

namespace AIThread.Service.Orchestration;

// First line of every orchestrated reply. It stays in the Claude Code conversation, so later requests can tell which
// AIT threads the chat contains — also after a rewind, a compaction or a resume, since only the lines still in
// the history are found. Replies written before the rename carry "**AiThread**"; those are still recognised.
public static partial class ThreadAnchor
{
    public static string Line(string projectName, Guid threadId) =>
        $"**AIT** · `{projectName}` · thread `{ShortId(threadId)}`";

    public static string ShortId(Guid threadId) => threadId.ToString("N")[..8];

    public static IEnumerable<string> Find(string text) =>
        string.IsNullOrEmpty(text) ? [] : Pattern().Matches(text).Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"\*\*(?:AIT|AiThread)\*\*[^\r\n]*?thread `([0-9a-f]{8})`")]
    private static partial Regex Pattern();
}
