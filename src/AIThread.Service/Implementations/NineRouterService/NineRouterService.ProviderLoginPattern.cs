using System.Text.RegularExpressions;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    [GeneratedRegex(@"authentication_error|\[401\]|\boauth\b|re-?authenticate|invalid[ _-]?(?:x-)?api[ _-]?key", RegexOptions.IgnoreCase)]
    private static partial Regex ProviderLoginPattern();
}
