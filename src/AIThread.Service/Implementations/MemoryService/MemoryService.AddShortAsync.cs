using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Memory;
using AIThread.Data.Cache;
using AIThread.Service.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryService
{
    public async Task<MemoryItemDto> AddShortAsync(SaveMemoryRequest request, Guid? sourceThreadId = null, CancellationToken cancellationToken = default)
    {
        var projects = await LocalProjectsAsync(cancellationToken);
        var owner = request.Type == MemoryType.Knowledge ? null : projects.Find(request.ProjectId);
        var label = await PrepareLabelAsync(request, owner, cancellationToken);
        var record = new ShortMemoryRecord(
            Guid.NewGuid(),
            request.Type == MemoryType.Knowledge ? null : request.ProjectId,
            request.Type,
            label.Composed,
            request.Description.Trim(),
            sourceThreadId,
            DateTime.UtcNow,
            owner?.Key,
            owner?.Name,
            label.Module,
            MemoryLabels.LayersCsv(label.Layers),
            label.Kind,
            label.Area,
            MemoryLabels.JoinCsv(label.Keywords),
            MemoryLabels.CurrentVersion,
            ExtraModules: MemoryLabels.JoinCsv(label.ExtraModules));

        if (shortStore.IsConnected)
        {
            try
            {
                await shortStore.SaveAsync(record);
                Publish("created", record.Id);
                return ToDto(record, projects);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Short memory write failed; storing memory directly as long memory.");
            }
        }

        // Short memory store unavailable: keep the memory rather than losing it.
        var entity = await InsertLongAsync(record, cancellationToken);
        activityLog.Warn("memory", $"Penyimpanan short memory tidak tersedia — memory '{record.Label}' langsung disimpan sebagai long memory.", sourceThreadId, record.ProjectId);
        Publish("created", entity.Id);
        return ToDto(entity, projects);
    }
}
