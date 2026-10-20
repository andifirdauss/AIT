using System.Text.RegularExpressions;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    [GeneratedRegex(@"^\s*base_url\s*=\s*""(?<url>[^""]*)""")]
    private static partial Regex BaseUrlLine();
}
