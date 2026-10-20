using System.Text.RegularExpressions;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    [GeneratedRegex(@"^\[\[?(?<name>[^\]]+)\]\]?\s*(#.*)?$")]
    private static partial Regex TableHeader();
}
