using System.Text.RegularExpressions;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Testing;

// "jalankan test lanjutan 2" (also "test lanjutan 2", "lanjut test 2", "run follow-up 2"): the developer runs one of the
// tester's suggestions from the chat.
public static partial class TestFollowUpReply
{
    public static int? Parse(string? reply)
    {
        if (string.IsNullOrWhiteSpace(reply)) return null;
        return Pattern().Match(reply.Trim()) is { Success: true } match && int.TryParse(match.Groups[1].Value, out var number) && number > 0 ? number : null;
    }

    // The suggestion with that number in a thread's stored report, or null.
    public static TestFollowUp? Find(string? testingJson, int number) =>
        TestingReport.FromJson(testingJson)?.FollowUps.FirstOrDefault(f => f.Number == number);

    [GeneratedRegex(@"^(?:jalankan\s+|run\s+)?(?:test\s+lanjutan|lanjut(?:an)?\s+test|follow[- ]?up(?:\s+test)?)\s*(?:no\.?\s*|nomor\s*|#)?(\d{1,2})\s*[.!]?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();
}
