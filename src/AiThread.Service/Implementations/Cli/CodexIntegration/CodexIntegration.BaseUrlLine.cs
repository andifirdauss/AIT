using System.Text.RegularExpressions;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    [GeneratedRegex(@"^\s*base_url\s*=\s*""(?<url>[^""]*)""")]
    private static partial Regex BaseUrlLine();
}
