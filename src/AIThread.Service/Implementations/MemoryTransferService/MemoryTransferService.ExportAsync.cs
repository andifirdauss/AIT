using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class MemoryTransferService
{
    public async Task<MemoryExportFile> ExportAsync(MemoryQuery filter, CancellationToken cancellationToken = default)
    {
        var projects = await LocalProjectsAsync(cancellationToken);
        var scope = filter.ProjectId is { } projectId ? projects.Find(projectId) ?? new LocalProject(projectId, null, string.Empty) : null;
        var search = filter.Search?.Trim();
        var items = new List<MemoryExportItem>();

        if (filter.Level is null or MemoryLevel.Short)
        {
            if (!shortStore.IsConnected)
                throw new AppValidationException(Lang.T("Penyimpanan short memory tidak terhubung — pilih level Long untuk mengekspor long memory saja.", "The short memory storage is not connected — choose the Long level to export long memory only."));

            items.AddRange((await shortStore.GetAllAsync())
                .Where(r => (filter.Type is null || r.Type == filter.Type)
                    && (scope is null || scope.Owns(r.ProjectKey, r.ProjectId))
                    && Matches(search, r.Label, r.Description))
                .Select(r => ToItem(projects, r.Id, MemoryLevel.Short, r.Type, r.ProjectId, r.ProjectKey, r.ProjectName,
                    r.Label, r.Description, r.SourceThreadId, r.CreatedAt, null, 0, null,
                    Label(r.LabelVersion, r.Module, r.Layers, r.Kind, r.Area, r.Keywords, r.ExtraModules), r.LegacyLabel)));
        }

        if (filter.Level is null or MemoryLevel.Long)
        {
            await using var db = await longStore.CreateAsync(cancellationToken);
            var query = db.LongMemories.AsNoTracking();
            if (filter.Type is { } type) query = query.Where(m => m.Type == type);
            if (scope is not null)
            {
                var key = scope.Key;
                var id = scope.Id;
                query = query.Where(m => (key != null && m.ProjectKey == key) || (m.ProjectKey == null && m.ProjectId == id));
            }
            if (!string.IsNullOrEmpty(search)) query = query.Where(m => m.Label.Contains(search) || m.Description.Contains(search));

            items.AddRange((await query.ToListAsync(cancellationToken)).Select(m => ToItem(projects, m.Id, MemoryLevel.Long, m.Type,
                m.ProjectId, m.ProjectKey, m.ProjectName, m.Label, m.Description, m.SourceThreadId, m.CreatedAt, m.PromotedAt,
                m.HitCount, m.LastAccessedAt, Label(m.LabelVersion, m.Module, m.Layers, m.Kind, m.Area, m.Keywords, m.ExtraModules), m.LegacyLabel)));
        }

        // The file leaves this machine: a secret that slipped into a memory (written by an older version) stays masked.
        return new MemoryExportFile(MemoryExportFile.FormatName, MemoryExportFile.CurrentVersion, DateTime.UtcNow, AppVersion(),
            items.OrderBy(i => i.CreatedAt).Select(i => i with { Description = secrets.Redact(i.Description) }).ToList(),
            await ExportUnitTestsAsync(filter, scope, cancellationToken),
            await ExportTestingAsync(filter, scope, cancellationToken));
    }
}
