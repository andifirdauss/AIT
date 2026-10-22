using System.Text.RegularExpressions;

namespace AIThread.Service.Testing;

public sealed partial class AppLauncher
{
    // "...csproj : warning NU1903: ...", "Program.cs(12,5): warning CS8618: ...", and npm's "npm warn ...".
    [GeneratedRegex(@"(:\s*warning\s+[A-Z]+\d+\s*:)|^\s*npm\s+warn\b", RegexOptions.IgnoreCase)]
    private static partial Regex BuildWarning();
}
