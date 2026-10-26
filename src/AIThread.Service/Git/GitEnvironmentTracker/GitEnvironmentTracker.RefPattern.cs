using System.Text.RegularExpressions;

namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // A branch, remote branch or tag such as "origin/main" or "release/2.1": never an option.
    [GeneratedRegex(@"^[A-Za-z0-9_][\w./@+-]{0,249}$")]
    private static partial Regex RefPattern();
}
