using AIThread.Common.DTOs;
using AIThread.Service.Diagnostics;

namespace AIThread.Service.Interfaces;

// Log menu: incidents (errors with stack trace and companion files, kept on disk), the run logs, and the diagnostic ZIP.
public interface IDiagnosticsService
{
    string IncidentsRoot { get; }

    string LogRoot { get; }

    // Never throws: a failure to record is only lost. The same error within a few minutes is folded into the earlier
    // incident (Count + 1). Returns the incident id, or null when nothing was written.
    string? Record(DiagnosticReport report);

    Task<IReadOnlyList<DiagnosticIncidentSummaryDto>> ListIncidentsAsync(Guid? threadId, string? search, int take, CancellationToken cancellationToken = default);

    Task<DiagnosticIncidentDto?> GetIncidentAsync(string id, CancellationToken cancellationToken = default);

    Task<string?> ReadIncidentFileAsync(string id, string name, CancellationToken cancellationToken = default);

    // id null: every incident.
    int DeleteIncidents(string? id);

    IReadOnlyList<LogFileDto> ListLogFiles();

    Task<LogFileTailDto?> ReadLogAsync(string name, int tail, string? search, CancellationToken cancellationToken = default);

    // Writes the ZIP to output; returns its file name.
    Task<string> ExportAsync(DiagnosticExportRequest request, Stream output, CancellationToken cancellationToken = default);
}
