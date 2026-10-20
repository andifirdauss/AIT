using System.Text.RegularExpressions;

namespace AiThread.Service.Skills;

public sealed partial class SkillFetcher
{
    [GeneratedRegex(@"href\s*=\s*[""'](?<url>[^""'#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Href();
}
