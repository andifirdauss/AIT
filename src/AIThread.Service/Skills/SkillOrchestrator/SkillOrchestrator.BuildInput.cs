using System.Text;

namespace AIThread.Service.Skills;

public sealed partial class SkillOrchestrator
{
    private static string BuildInput(string prompt, string? role, IReadOnlyList<SkillCandidate> candidates)
    {
        var builder = new StringBuilder();
        builder.Append("# Developer request\n").Append(prompt).Append("\n\n");
        builder.Append("# Node role\n").Append(role ?? "(not chosen yet)").Append("\n\n");
        builder.Append("# Skills in the store\n");
        for (var i = 0; i < candidates.Count; i++)
        {
            var skill = candidates[i];
            var description = skill.Description.Length <= DescriptionChars ? skill.Description : skill.Description[..DescriptionChars] + "…";
            builder.Append($"[{i + 1}] {skill.Name} ({skill.SourceName}) — {description}");
            if (!string.IsNullOrWhiteSpace(skill.Tags)) builder.Append($" [tags: {skill.Tags}]");
            builder.Append('\n');
        }
        return builder.ToString();
    }
}
