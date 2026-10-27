using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Git;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // For the planner: project memory whose change was thrown away or lost is left out, and memory whose change is not in
    // the branch checked out in this clone gets a note (GitChangeTracker.PresenceAsync). Knowledge, memory without a
    // thread and memory git knows nothing of stay as they are.
    private async Task<List<MemoryIndexEntry>> WithGitAsync(ProjectScope? scope, List<MemoryIndexEntry> entries, CancellationToken cancellationToken)
    {
        var presence = await PresenceAsync(scope, entries.Where(e => e.Type == MemoryType.Project).Select(e => e.SourceThreadId), cancellationToken);
        if (presence.Count == 0) return entries;
        return entries
            .Where(e => e.SourceThreadId is not { } id || !presence.TryGetValue(id, out var p) || p.Place != MemoryGitPlace.Gone)
            .Select(e => e.SourceThreadId is { } id && presence.TryGetValue(id, out var p) && GitNotes.ForPlanner(p) is { } note ? e with { GitNote = note } : e)
            .ToList();
    }

    private async Task<List<MemoryItemDto>> WithGitAsync(ProjectScope? scope, List<MemoryItemDto> items, CancellationToken cancellationToken)
    {
        var presence = await PresenceAsync(scope, items.Where(i => i.Type == MemoryType.Project).Select(i => i.SourceThreadId), cancellationToken);
        if (presence.Count == 0) return items;
        return items
            .Where(i => i.SourceThreadId is not { } id || !presence.TryGetValue(id, out var p) || p.Place != MemoryGitPlace.Gone)
            .Select(i => i.SourceThreadId is { } id && presence.TryGetValue(id, out var p)
                ? i with { GitState = p.State, GitPlace = p.Place, GitNote = GitNotes.ForPlanner(p) }
                : i)
            .ToList();
    }
}
