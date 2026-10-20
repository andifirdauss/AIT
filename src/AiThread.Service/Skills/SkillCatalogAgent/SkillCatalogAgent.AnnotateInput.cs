using System.Text;

namespace AiThread.Service.Skills;

public sealed partial class SkillCatalogAgent
{
    private static string AnnotateInput(string sourceName, IReadOnlyList<DiscoveredSkill> batch)
    {
        var builder = new StringBuilder();
        builder.Append($"# Source\n{sourceName}\n\n# Skills\n");
        for (var i = 0; i < batch.Count; i++)
        {
            var skill = batch[i];
            var excerpt = skill.Body.Length <= ExcerptChars ? skill.Body : skill.Body[..ExcerptChars] + " …";
            builder.Append($"\n## [{i + 1}] {skill.Name}\nPath: {skill.RelativeDir}/{skill.EntryFile}\n");
            builder.Append($"Declared description: {(string.IsNullOrWhiteSpace(skill.Description) ? "(none)" : skill.Description)}\n");
            builder.Append("Content start:\n").Append(excerpt).Append('\n');
        }
        return builder.ToString();
    }
}
