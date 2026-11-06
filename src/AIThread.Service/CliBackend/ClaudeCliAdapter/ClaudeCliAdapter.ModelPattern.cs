using System.Text.RegularExpressions;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    [GeneratedRegex(@"model.{0,40}(not found|not available|invalid|does not exist|unknown)|(unknown|invalid) model", RegexOptions.IgnoreCase)]
    private static partial Regex ModelPattern();
}
