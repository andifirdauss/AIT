using System.Text.RegularExpressions;

namespace AIThread.Service.Orchestration;

// A chat reply that runs the unfinished tasks of the chat's last thread again from its stored plan, like
// "🔁 Ulangi yang gagal" on the thread page. Only a short reply of its own counts — "ulangi", "coba lagi", "retry",
// optionally with "yang gagal" — so a request that starts with these words still reaches the planner.
public static partial class RetryReply
{
    public static bool IsMatch(string? reply) => !string.IsNullOrWhiteSpace(reply) && Pattern().IsMatch(reply);

    [GeneratedRegex(@"^\s*(?:ulangi|ulang|coba\s+(?:lagi|ulang)|retry)(?:\s+(?:tugas\s+)?yang\s+gagal)?\s*[.!]*\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
