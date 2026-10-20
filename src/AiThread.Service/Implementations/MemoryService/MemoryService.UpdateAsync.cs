using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Common.Memory;
using Microsoft.EntityFrameworkCore;

namespace AiThread.Service.Implementations;

public sealed partial class MemoryService
{
    // Saving from the form labels an unlabeled (or to-be-reviewed) memory for good; its old label is kept as LegacyLabel.
    public async Task<MemoryItemDto?> UpdateAsync(MemoryLevel level, Guid id, SaveMemoryRequest request, CancellationToken cancellationToken = default)
    {
        var projects = await LocalProjectsAsync(cancellationToken);
        var projectId = request.Type == MemoryType.Knowledge ? null : request.ProjectId;
        var owner = projects.Find(projectId);

        if (level == MemoryLevel.Short)
        {
            EnsureShortStore();
            var record = await shortStore.GetAsync(id);
            if (record is null) return null;

            var keyOwner = owner ?? (record.ProjectKey is { } k ? new ProjectScope(Guid.Empty, k, record.ProjectName ?? string.Empty) : null);
            var label = await PrepareLabelAsync(request, request.Type == MemoryType.Knowledge ? null : keyOwner, cancellationToken);
            var updated = record with
            {
                Type = request.Type,
                ProjectId = projectId,
                ProjectKey = projectId is null ? null : owner?.Key ?? record.ProjectKey,
                ProjectName = projectId is null ? null : owner?.Name ?? record.ProjectName,
                Label = label.Composed,
                Description = request.Description.Trim(),
                Module = label.Module,
                ExtraModules = MemoryLabels.JoinCsv(label.ExtraModules),
                Layers = MemoryLabels.LayersCsv(label.Layers),
                Kind = label.Kind,
                Area = label.Area,
                Keywords = MemoryLabels.JoinCsv(label.Keywords),
                LabelVersion = MemoryLabels.CurrentVersion,
                LegacyLabel = record.LegacyLabel ?? (IsLabeled(record.LabelVersion, record.Module, record.Kind) ? null : record.Label),
                ReviewReason = null
            };
            await shortStore.SaveAsync(updated);
            Publish("updated", id);
            return ToDto(updated, projects);
        }

        await using var db = await longStore.CreateAsync(cancellationToken);
        var entity = await db.LongMemories.FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (entity is null) return null;

        var longOwner = owner ?? (entity.ProjectKey is { } key ? new ProjectScope(Guid.Empty, key, entity.ProjectName ?? string.Empty) : null);
        var longLabel = await PrepareLabelAsync(request, request.Type == MemoryType.Knowledge ? null : longOwner, cancellationToken);
        if (!IsLabeled(entity.LabelVersion, entity.Module, entity.Kind)) entity.LegacyLabel ??= entity.Label;
        entity.Type = request.Type;
        // The main database enforces the project foreign key: only a project registered here can be referenced.
        entity.ProjectId = owner?.Id;
        entity.ProjectKey = projectId is null ? null : owner?.Key ?? entity.ProjectKey;
        entity.ProjectName = projectId is null ? null : owner?.Name ?? entity.ProjectName;
        entity.Description = request.Description.Trim();
        ApplyLabel(entity, longLabel);
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        Publish("updated", id);
        return ToDto(entity, projects);
    }
}
