using System.Text.RegularExpressions;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    [GeneratedRegex(@"^(?<key>[A-Za-z0-9_\-.""' ]+?)\s*=")]
    private static partial Regex KeyLine();
}
