using System.Text.RegularExpressions;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class OhMyPiProviderIntegration
{
    [GeneratedRegex(@"^\s*baseUrl:\s*""?(?<url>[^""\s#]+)""?")]
    private static partial Regex BaseUrlLine();
}
