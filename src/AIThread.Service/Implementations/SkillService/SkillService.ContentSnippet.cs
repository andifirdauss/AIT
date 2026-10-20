namespace AIThread.Service.Implementations;

public sealed partial class SkillService
{
    private static string? ContentSnippet(string content, string[] terms, int radius = 70)
    {
        foreach (var term in terms)
        {
            var index = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (index < 0) continue;
            var start = Math.Max(0, index - radius);
            var end = Math.Min(content.Length, index + term.Length + radius);
            var text = string.Join(' ', content[start..end].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            return $"{(start > 0 ? "…" : string.Empty)}{text}{(end < content.Length ? "…" : string.Empty)}";
        }

        return null;
    }
}
