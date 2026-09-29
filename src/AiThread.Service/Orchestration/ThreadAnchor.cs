using System.Text.RegularExpressions;

namespace AiThread.Service.Orchestration;

// First line of every orchestrated reply. It stays in the Claude Code conversation, so later requests can tell which
// AiThread threads the chat contains — also after a rewind, a compaction or a resume, since only the lines still in
// the history are found.
public static partial class ThreadAnchor
{
    public static string Line(string solutionFile, Guid threadId) =>
        $"**AiThread** · `{solutionFile}` · thread `{ShortId(threadId)}`";

    public static string ShortId(Guid threadId) => threadId.ToString("N")[..8];

    public static IEnumerable<string> Find(string text) =>
        string.IsNullOrEmpty(text) ? [] : Pattern().Matches(text).Select(m => m.Groups[1].Value);

    [GeneratedRegex(@"\*\*AiThread\*\*[^\r\n]*?thread `([0-9a-f]{8})`")]
    private static partial Regex Pattern();
}
