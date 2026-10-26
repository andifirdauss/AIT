using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Data.Cache;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    // Idempotent: the long row keeps the short memory id, so a retried consolidation never duplicates.
    private async Task<LongMemory> InsertLongAsync(ShortMemoryRecord record, CancellationToken cancellationToken)
    {
        await using var db = await longStore.CreateAsync(cancellationToken);
        var existing = await db.LongMemories.FirstOrDefaultAsync(m => m.Id == record.Id, cancellationToken);
        if (existing is not null) return existing;

        // The project may have been deleted while the memory waited in short memory. Its key keeps the memory with
        // the project's other clones; without a key (older records) it turns into knowledge as before.
        var projects = await LocalProjectsAsync(cancellationToken);
        var owner = projects.Find(record.ProjectId);
        var projectKey = record.Type == MemoryType.Knowledge ? null : record.ProjectKey ?? owner?.Key;
        var projectId = owner?.Id ?? (projectKey is null ? null : projects.FindByKey(projectKey)?.Id);
        var git = projectKey is null ? null : await GitOriginAsync(record.SourceThreadId, cancellationToken);

        var entity = new LongMemory
        {
            Id = record.Id,
            ProjectId = projectId,
            ProjectKey = projectKey,
            ProjectName = projectKey is null ? null : record.ProjectName ?? owner?.Name,
            Type = projectId is null && projectKey is null ? MemoryType.Knowledge : record.Type,
            Label = record.Label,
            Description = record.Description,
            Module = record.Module,
            ExtraModules = record.ExtraModules,
            Layers = record.Layers,
            Kind = record.Kind,
            Area = record.Area,
            Keywords = record.Keywords,
            LabelVersion = record.LabelVersion,
            LegacyLabel = record.LegacyLabel,
            ReviewReason = record.ReviewReason,
            RelabelAttempts = record.RelabelAttempts,
            SourceThreadId = record.SourceThreadId,
            GitAuthor = git?.GitAuthor,
            GitBranch = git?.GitBranch,
            GitCommit = git?.GitBaseCommit,
            CreatedAt = record.CreatedAt,
            PromotedAt = DateTime.UtcNow
        };
        db.LongMemories.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }
}
