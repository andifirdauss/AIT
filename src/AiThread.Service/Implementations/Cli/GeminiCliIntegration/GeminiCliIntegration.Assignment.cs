using System.Text.RegularExpressions;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class GeminiCliIntegration
{
    [GeneratedRegex(@"^\s*(export\s+)?(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*[""']?(?<value>[^""'\s#]*)")]
    private static partial Regex Assignment();
}
