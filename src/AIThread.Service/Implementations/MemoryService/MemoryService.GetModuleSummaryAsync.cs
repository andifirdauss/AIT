using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<IReadOnlyList<MemoryModuleSummary>> GetModuleSummaryAsync(Guid? projectId, CancellationToken cancellationToken = default)
    {
        var scope = (await LocalProjectsAsync(cancellationToken)).Find(projectId);
        var entries = (await SafeShortAsync())
            .Where(r => r.Type == MemoryType.Knowledge || (scope is not null && scope.Owns(r.ProjectKey, r.ProjectId)))
            .Select(ToIndexEntry)
            .Concat(await LongIndexAsync(scope, cancellationToken))
            .Where(e => e.Labeled)
            .ToList();

        // A memory of several modules counts for each of them.
        return entries
            .SelectMany(e => e.Modules.Select(module => (Module: module, Entry: e)))
            .GroupBy(x => (x.Module, x.Entry.Type == MemoryType.Knowledge), x => x.Entry)
            .Select(g => new MemoryModuleSummary(
                g.Key.Item1,
                g.Key.Item2,
                g.Count(),
                g.SelectMany(e => e.Layers ?? []).GroupBy(l => l).ToDictionary(x => x.Key, x => x.Count()),
                g.GroupBy(e => e.Kind!.Value).ToDictionary(x => x.Key, x => x.Count())))
            .OrderBy(s => s.Knowledge)
            .ThenByDescending(s => s.Count)
            .ThenBy(s => s.Module)
            .ToList();
    }
}
