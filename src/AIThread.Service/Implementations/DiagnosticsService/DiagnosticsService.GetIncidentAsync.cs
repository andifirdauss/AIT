using AIThread.Common.DTOs;
using AIThread.Service.Diagnostics;

namespace AIThread.Service.Implementations;

public sealed partial class DiagnosticsService
{
    public Task<DiagnosticIncidentDto?> GetIncidentAsync(string id, CancellationToken cancellationToken = default)
    {
        if (IncidentFolders.FolderOf(IncidentsRoot, id) is not { } folder || IncidentFolders.Read(folder) is not { } incident)
            return Task.FromResult<DiagnosticIncidentDto?>(null);
        return Task.FromResult<DiagnosticIncidentDto?>(new DiagnosticIncidentDto(incident.Id, incident.CreatedAt, incident.LastAt, incident.Count,
            incident.Level, incident.Categories, incident.Title, incident.Message, incident.ThreadId, incident.ProjectId, incident.ExceptionType,
            incident.StackTrace, IncidentFolders.Files(folder).Select(f => new DiagnosticFileDto(f.Name, f.Length)).ToList()));
    }
}
