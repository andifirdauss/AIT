using System.Text.RegularExpressions;

namespace AiThread.Service.Skills;

public sealed partial class SkillFetcher
{
    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex Spaces();
}
