using System.Text.RegularExpressions;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    [GeneratedRegex(@"rate.?limit|usage limit|limit reached|too many requests|quota", RegexOptions.IgnoreCase)]
    private static partial Regex RateLimitPattern();
}
