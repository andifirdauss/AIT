using System.Text.RegularExpressions;

namespace AiThread.Service.Skills;

public sealed partial class SkillFetcher
{
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();
}
