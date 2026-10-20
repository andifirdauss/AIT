using System.Text.RegularExpressions;

namespace AiThread.Service.Skills;

public sealed partial class SkillFetcher
{
    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/h[1-6]|/tr|/pre|/section|/article)\b[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockTag();
}
