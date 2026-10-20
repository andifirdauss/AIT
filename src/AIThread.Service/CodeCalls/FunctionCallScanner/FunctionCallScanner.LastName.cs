using System.Text.RegularExpressions;

namespace AIThread.Service.CodeCalls;

public sealed partial class FunctionCallScanner
{
    [GeneratedRegex(@"[A-Za-z_$][\w$]*(?=\W*$)")]
    private static partial Regex LastName();
}
