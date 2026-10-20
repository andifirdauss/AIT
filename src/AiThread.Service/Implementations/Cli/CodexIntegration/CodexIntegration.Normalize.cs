using System.Text.RegularExpressions;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    private static string Normalize(string dotted) => Regex.Replace(dotted, @"[\s""']", string.Empty);
}
