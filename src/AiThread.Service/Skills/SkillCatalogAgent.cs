using System.Text;
using System.Text.Json.Nodes;
using AiThread.Service.Models;
using AiThread.Service.Orchestration;

namespace AiThread.Service.Skills;

public sealed record WebSkill(string Name, string Description, string? Tags, string Content);

public sealed record WebExtraction(IReadOnlyList<WebSkill> Skills, IReadOnlyList<string> Sources);

// The skill orchestrator's look at a pulled source: tags the SKILL.md files it found, points out the skills in a
// repository without SKILL.md files, and reads skills and repository links off a web page.
public sealed class SkillCatalogAgent(SkillAi ai)
{
    private const int AnnotateBatch = 40;
    private const int ParallelBatches = 4;
    private const int ExcerptChars = 1_200;
    private const int MaxTreeEntries = 500;
    private const int ReadmeChars = 8_000;

    // Same order as skills; skills the model dropped are removed, the others get tags (and a description when missing).
    // Large repositories (hundreds of SKILL.md) take many batches, so a few run at once; onProgress gets (done, total).
    // A batch the model fails on keeps its skills untagged instead of failing the whole pull.
    public async Task<IReadOnlyList<DiscoveredSkill>> AnnotateAsync(
        string sourceName, IReadOnlyList<DiscoveredSkill> skills, Func<int, int, Task>? onProgress, CancellationToken cancellationToken)
    {
        var batches = skills.Chunk(AnnotateBatch).ToList();
        var results = new IReadOnlyList<DiscoveredSkill>[batches.Count];
        var done = 0;
        var failed = 0;
        using var gate = new SemaphoreSlim(ParallelBatches);

        async Task RunAsync(int index)
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                var batch = batches[index];
                try
                {
                    var (json, _, _) = await ai.AskJsonAsync(AgentPrompts.SkillAnnotate, AnnotateInput(sourceName, batch), 6_000, cancellationToken);
                    results[index] = ApplyAnnotations(batch, json);
                }
                catch (NineRouterException)
                {
                    Interlocked.Increment(ref failed);
                    results[index] = batch;
                }
                if (onProgress is not null) await onProgress(Interlocked.Increment(ref done), batches.Count);
            }
            finally
            {
                gate.Release();
            }
        }

        await Task.WhenAll(Enumerable.Range(0, batches.Count).Select(RunAsync));
        // Nothing got through: let the caller report the orchestrator as unreachable.
        if (failed == batches.Count && batches.Count > 0)
            throw new NineRouterException("Skill Orchestrator gagal pada semua batch.");
        return results.SelectMany(r => r).ToList();
    }

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
