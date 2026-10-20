using System.Text.RegularExpressions;

namespace AIThread.Service.Skills;

public sealed partial class SkillFetcher
{
    [GeneratedRegex(@"href\s*=\s*[""'](?<url>[^""'#]+)", RegexOptions.IgnoreCase)]
    private static partial Regex Href();
}
