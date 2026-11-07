using System.Text.RegularExpressions;

namespace AIThread.Service.CliBackend;

public sealed partial class CodexCliAdapter
{
    [GeneratedRegex("""^(profile|model_provider)\s*=\s*["'](?<value>[^"']*)["']""")]
    private static partial Regex TopLevelKey();
}
