using System.Text.RegularExpressions;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    [GeneratedRegex(@"^\s*(GET|POST|PUT|DELETE|PATCH|HEAD|OPTIONS)\s+", RegexOptions.IgnoreCase)]
    private static partial Regex HttpPrefix();
}
