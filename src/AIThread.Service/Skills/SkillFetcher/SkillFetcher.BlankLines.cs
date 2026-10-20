using System.Text.RegularExpressions;

namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    [GeneratedRegex(@"\s*\n\s*\n\s*")]
    private static partial Regex BlankLines();
}
