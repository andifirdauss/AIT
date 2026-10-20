using System.Text.RegularExpressions;

namespace AIThread.Service.Implementations;

public sealed partial class NineRouterService
{
    [GeneratedRegex(@"""message""\s*:\s*""((?:[^""\\]|\\.)+)""")]
    private static partial Regex ProviderMessagePattern();
}
