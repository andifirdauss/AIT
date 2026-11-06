using System.Text.RegularExpressions;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    [GeneratedRegex(@"/login|not logged in|log ?in again|invalid api key|authentication|oauth token|unauthori[sz]ed|credential", RegexOptions.IgnoreCase)]
    private static partial Regex LoginPattern();
}
