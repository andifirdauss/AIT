using System.Text.RegularExpressions;

namespace AIThread.Service.Implementations;

public sealed partial class ThreadService
{
    // The start of a thread id as text ("1a2b3c4d", "1a2b3c4d-5e6f"): at least 4 hex digits, dashes allowed.
    [GeneratedRegex(@"^[0-9a-fA-F][0-9a-fA-F-]{3,35}$")]
    private static partial Regex IdPrefixPattern();
}
