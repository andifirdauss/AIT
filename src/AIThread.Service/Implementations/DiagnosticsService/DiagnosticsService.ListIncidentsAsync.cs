using AIThread.Common.DTOs;
using AIThread.Service.Diagnostics;

namespace AIThread.Service.Implementations;

public sealed partial class DiagnosticsService
{
    public Task<IReadOnlyList<DiagnosticIncidentSummaryDto>> ListIncidentsAsync(Guid? threadId, string? search, int take, CancellationToken cancellationToken = default)
    {
        var text = search?.Trim();
        IReadOnlyList<DiagnosticIncidentSummaryDto> list = IncidentFolders.Load(IncidentsRoot)
            .Where(x => threadId is null || x.Incident.ThreadId == threadId)
            .Where(x => string.IsNullOrEmpty(text)
                        || x.Incident.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
                        || x.Incident.Categories.Any(c => c.Contains(text, StringComparison.OrdinalIgnoreCase))
                        || (x.Incident.Message?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false)
                        || (x.Incident.StackTrace?.Contains(text, StringComparison.OrdinalIgnoreCase) ?? false))
            .Take(Math.Clamp(take, 1, 500))
            .Select(x => new DiagnosticIncidentSummaryDto(x.Incident.Id, x.Incident.CreatedAt, x.Incident.LastAt, x.Incident.Count, x.Incident.Level,
                string.Join(", ", x.Incident.Categories), x.Incident.Title, x.Incident.ThreadId, x.Incident.ProjectId, x.Incident.ExceptionType,
                IncidentFolders.Files(x.Path).Count))
            .ToList();
        return Task.FromResult(list);
    }
}
