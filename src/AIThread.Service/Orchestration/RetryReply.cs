using System.Text.RegularExpressions;

namespace AIThread.Service.Orchestration;

// A chat reply that runs the unfinished tasks of the chat's last thread again from its stored plan, like
// "🔁 Ulangi yang gagal" on the thread page. Only a short reply of its own counts — "ulangi", "coba lagi", "retry",
// "try again", optionally with "yang gagal" / "failed (tasks)" — so a request that starts with these words still
// reaches the planner.
public static partial class RetryReply
{
    public static bool IsMatch(string? reply) => !string.IsNullOrWhiteSpace(reply) && Pattern().IsMatch(reply);

    [GeneratedRegex(@"^\s*(?:ulangi|ulang|coba\s+(?:lagi|ulang)|retry|try\s+again)(?:\s+(?:tugas\s+)?yang\s+gagal|\s+(?:the\s+)?failed(?:\s+tasks?)?)?\s*[.!]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
