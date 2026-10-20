using AiThread.Service.Skills;

namespace AiThread.Service.Interfaces;

public interface ISkillCatalogAgent
{
    Task<IReadOnlyList<DiscoveredSkill>> AnnotateAsync(string sourceName, IReadOnlyList<DiscoveredSkill> skills, Func<int, int, Task>? onProgress, CancellationToken cancellationToken);

    Task<IReadOnlyList<(string Path, string? Name, string? Description, string? Tags)>> IdentifyAsync(string sourceName, string root, CancellationToken cancellationToken);

    Task<WebExtraction> ExtractFromWebAsync(string url, WebPage page, CancellationToken cancellationToken);
}
