using System.Text.RegularExpressions;

namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();
}
