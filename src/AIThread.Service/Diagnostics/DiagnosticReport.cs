namespace AIThread.Service.Diagnostics;

// A companion file of an incident: text AIT had at hand when the error happened (raw model output, the task message).
public sealed record DiagnosticAttachment(string Name, string Content);

// What a caller hands IDiagnosticsService.Record. Level: "Error" or "Warning". Exception: kept as its full text,
// inner exceptions and stack traces included.
public sealed record DiagnosticReport(
    string Level,
    string Category,
    string Title,
    string? Message = null,
    Guid? ThreadId = null,
    Guid? ProjectId = null,
    Exception? Exception = null,
    IReadOnlyList<DiagnosticAttachment>? Files = null);

// incident.json of one incident folder.
public sealed class DiagnosticIncident
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastAt { get; set; }
    public int Count { get; set; } = 1;
    public string Level { get; set; } = "Error";
    public List<string> Categories { get; set; } = [];
    public string Title { get; set; } = string.Empty;
    public string? Message { get; set; }
    public Guid? ThreadId { get; set; }
    public Guid? ProjectId { get; set; }
    public string? ExceptionType { get; set; }
    public string? StackTrace { get; set; }
    public string? AppVersion { get; set; }
}
