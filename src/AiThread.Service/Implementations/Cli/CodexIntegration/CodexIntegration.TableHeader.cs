using System.Text.RegularExpressions;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    [GeneratedRegex(@"^\[\[?(?<name>[^\]]+)\]\]?\s*(#.*)?$")]
    private static partial Regex TableHeader();
}
