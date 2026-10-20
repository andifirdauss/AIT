using System.Text.RegularExpressions;

namespace AIThread.Service.Skills;

public sealed partial class SkillImporter
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonSlug();
}
