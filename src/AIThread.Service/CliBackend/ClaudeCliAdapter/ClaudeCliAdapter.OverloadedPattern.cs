using System.Text.RegularExpressions;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    [GeneratedRegex(@"overloaded|internal server error|service unavailable|bad gateway", RegexOptions.IgnoreCase)]
    private static partial Regex OverloadedPattern();
}
