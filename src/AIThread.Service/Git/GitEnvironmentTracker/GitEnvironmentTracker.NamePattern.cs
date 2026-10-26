using System.Text.RegularExpressions;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // "dev", "staging2", "prod-eu": letters, digits, '_', '.', '-'.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]{0,49}$")]
    private static partial Regex NamePattern();
}
