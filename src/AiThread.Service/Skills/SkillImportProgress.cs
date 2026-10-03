using System.Collections.Concurrent;

namespace AiThread.Service.Skills;

// Live progress text of sources being pulled (e.g. "batch 3/22"), shown next to their status. Memory only:
// it only matters while a pull runs.
public sealed class SkillImportProgress
{
    private readonly ConcurrentDictionary<Guid, string> _progress = new();

    public void Set(Guid sourceId, string text) => _progress[sourceId] = text;

    public void Clear(Guid sourceId) => _progress.TryRemove(sourceId, out _);

    public string? Get(Guid sourceId) => _progress.TryGetValue(sourceId, out var text) ? text : null;
}
