using System.Text;
using System.Text.Json.Nodes;
using AiThread.Service.Orchestration;

namespace AiThread.Service.Skills;

public sealed partial class SkillCatalogAgent
{
    public async Task<WebExtraction> ExtractFromWebAsync(string url, WebPage page, CancellationToken cancellationToken)
    {
        var input = new StringBuilder();
        input.Append($"# Page\n{url}\n\n# Links\n");
        input.Append(page.Links.Count == 0 ? "(none)\n" : string.Concat(page.Links.Select(l => $"- {l}\n")));
        input.Append("\n# Page text\n").Append(page.Text);

        var (json, _, _) = await ai.AskJsonAsync(AgentPrompts.SkillWebExtract, input.ToString(), 16_000, cancellationToken);
        var skills = new List<WebSkill>();
        foreach (var item in json?["skills"] as JsonArray ?? [])
        {
            var name = SkillAi.Text(item?["name"]);
            var content = SkillAi.Text(item?["content"]);
            if (name is null || content is null) continue;
            skills.Add(new WebSkill(name, SkillAi.Text(item?["description"]) ?? string.Empty, SkillAi.Text(item?["tags"]), content));
        }

        // Only links that were really on the page.
        var sources = (json?["sources"] as JsonArray ?? [])
            .Select(SkillAi.Text)
            .OfType<string>()
            .Where(s => page.Links.Contains(s.TrimEnd('/'), StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
        return new WebExtraction(skills, sources);
    }
}
