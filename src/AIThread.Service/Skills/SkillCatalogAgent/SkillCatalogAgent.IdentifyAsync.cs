using System.Text;
using System.Text.Json.Nodes;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Skills;

public sealed partial class SkillCatalogAgent
{
    public async Task<IReadOnlyList<(string Path, string? Name, string? Description, string? Tags)>> IdentifyAsync(
        string sourceName, string root, CancellationToken cancellationToken)
    {
        var tree = SkillDiscovery.FileTree(root, MaxTreeEntries);
        if (tree.Count == 0) return [];

        var input = new StringBuilder();
        input.Append($"# Source\n{sourceName}\n\n# Files\n");
        foreach (var file in tree) input.Append(file).Append('\n');
        if (SkillDiscovery.ReadmeExcerpt(root, ReadmeChars) is { } readme) input.Append("\n# README\n").Append(readme).Append('\n');

        var (json, _, _) = await ai.AskJsonAsync(AgentPrompts.SkillIdentify, input.ToString(), 12_000, cancellationToken);
        var found = new List<(string, string?, string?, string?)>();
        foreach (var item in json?["skills"] as JsonArray ?? [])
        {
            var path = SkillAi.Text(item?["path"]);
            if (path is null || !tree.Contains(path.TrimStart('/'), StringComparer.OrdinalIgnoreCase)) continue;
            found.Add((path.TrimStart('/'), SkillAi.Text(item?["name"]), SkillAi.Text(item?["description"]), SkillAi.Text(item?["tags"])));
        }
        return found;
    }
}
