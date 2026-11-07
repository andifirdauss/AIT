using System.Text.RegularExpressions;

namespace AIThread.Service.CliBackend;

public sealed partial class GeminiCliAdapter
{
    [GeneratedRegex(@"^\s*(export\s+)?(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*[""']?(?<value>[^""'\s#]*)")]
    private static partial Regex EnvAssignment();
}
