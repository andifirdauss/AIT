using System.Text;

namespace AiThread.Service.Skills;

public sealed partial class SkillImporter
{
    // Each skill written on a web page becomes <slug>/SKILL.md; returns the tags by folder.
    private static Dictionary<string, string?> WriteWebSkills(string staging, IReadOnlyList<WebSkill> skills)
    {
        var tags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var skill in skills)
        {
            var slug = Slug(skill.Name);
            for (var n = 2; tags.ContainsKey(slug); n++) slug = $"{Slug(skill.Name)}-{n}";
            var folder = Path.Combine(staging, slug);
            Directory.CreateDirectory(folder);

            var text = new StringBuilder();
            text.Append("---\n");
            text.Append($"name: {skill.Name.Replace('\n', ' ')}\n");
            text.Append($"description: \"{skill.Description.Replace("\"", "'").Replace('\n', ' ')}\"\n");
            text.Append("---\n\n").Append(skill.Content.Trim()).Append('\n');
            File.WriteAllText(Path.Combine(folder, SkillDiscovery.EntryFileName), text.ToString());
            tags[slug] = skill.Tags is null ? null : SkillDiscovery.Limit(skill.Tags, 500);
        }
        return tags;
    }
}
