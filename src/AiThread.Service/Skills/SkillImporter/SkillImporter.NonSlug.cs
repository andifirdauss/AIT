using System.Text.RegularExpressions;

namespace AiThread.Service.Skills;

public sealed partial class SkillImporter
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();
}
