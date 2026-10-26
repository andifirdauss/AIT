using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // The Memory menu's environment filter: project memory whose source change was not seen in the environment goes.
    private async Task<List<MemoryItemDto>> InEnvironmentAsync(List<MemoryItemDto> items, string projectKey, string environment,
        CancellationToken cancellationToken)
    {
        await using var db = await longStore.CreateGitAsync(cancellationToken);
        var threads = (await db.ChangePresences.AsNoTracking()
                .Where(p => p.ProjectKey == projectKey && p.Environment == environment)
                .Select(p => p.ThreadId)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        return items.Where(i => i.Type != MemoryType.Project || i.SourceThreadId is not { } thread || threads.Contains(thread)).ToList();
    }
}
