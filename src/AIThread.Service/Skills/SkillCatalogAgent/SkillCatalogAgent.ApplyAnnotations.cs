using System.Text.Json.Nodes;

namespace AIThread.Service.Skills;

public sealed partial class SkillCatalogAgent
{
    public static IReadOnlyList<DiscoveredSkill> ApplyAnnotations(IReadOnlyList<DiscoveredSkill> batch, JsonNode? json)
    {
        // An unreadable answer must not lose skills: keep them all, untagged.
        if (json?["skills"] is not JsonArray items) return batch;

        var byIndex = new Dictionary<int, JsonNode>();
        foreach (var item in items)
            if (item is not null && SkillAi.Int(item["index"]) is { } index) byIndex.TryAdd(index, item);

        var result = new List<DiscoveredSkill>();
        for (var i = 0; i < batch.Count; i++)
        {
            var skill = batch[i];
            if (!byIndex.TryGetValue(i + 1, out var note))
            {
                result.Add(skill);
                continue;
            }
            if (SkillAi.Bool(note["keep"]) == false) continue;

            var description = string.IsNullOrWhiteSpace(skill.Description) ? SkillAi.Text(note["description"]) ?? string.Empty : skill.Description;
            var tags = SkillAi.Text(note["tags"]);
            result.Add(skill with { Description = SkillDiscovery.Limit(description, 2000), Tags = tags is null ? null : SkillDiscovery.Limit(tags, 500) });
        }
        return result;
    }
}
