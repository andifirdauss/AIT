namespace AIThread.Common.DTOs;

// Log menu → Incidents: an error AIT recorded with its stack trace and companion files (the raw output of a failed
// executor, the task it got, ...). Count: how often the same error came within a short window (folded into one).
public record DiagnosticIncidentSummaryDto(
    string Id,
    DateTime CreatedAt,
    DateTime LastAt,
    int Count,
    string Level,
    string Category,
    string Title,
    Guid? ThreadId,
    Guid? ProjectId,
    string? ExceptionType,
    int FileCount);

public record DiagnosticFileDto(string Name, long Size);

public record DiagnosticIncidentDto(
    string Id,
    DateTime CreatedAt,
    DateTime LastAt,
    int Count,
    string Level,
    IReadOnlyList<string> Categories,
    string Title,
    string? Message,
    Guid? ThreadId,
    Guid? ProjectId,
    string? ExceptionType,
    string? StackTrace,
    IReadOnlyList<DiagnosticFileDto> Files);

// Log menu → Run logs: the log files AIThread.exe writes in menu/tray mode. Current: the file of this process.
public record LogFileDto(string Name, long Size, DateTime ModifiedAt, bool Current);

// The last lines of a run log (filtered when a search was given). Truncated: older lines were left out.
public record LogFileTailDto(string Name, IReadOnlyList<string> Lines, bool Truncated);

// Log menu → Export: what goes into the ZIP. Days: incidents, activity and run logs of the last days.
public record DiagnosticExportRequest(
    int Days = 3,
    bool Incidents = true,
    bool Activity = true,
    bool RunLogs = true,
    Guid? ThreadId = null);
