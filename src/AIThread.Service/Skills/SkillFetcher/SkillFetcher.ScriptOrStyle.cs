using System.Text.RegularExpressions;

namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    [GeneratedRegex(@"<(script|style|noscript|svg)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();
}
