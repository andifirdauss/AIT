using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Log menu: incidents (errors with stack trace and companion files), the run logs, and the diagnostic ZIP to bring a
// client's logs to whoever maintains AIT. The activity log itself is GET /api/dashboard/logs.
[ApiController]
[Route("api/diagnostics")]
public class DiagnosticsController(IDiagnosticsService diagnostics) : ControllerBase
{
    [HttpGet("incidents")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<DiagnosticIncidentSummaryDto>>>> Incidents(
        [FromQuery] Guid? threadId = null, [FromQuery] string? q = null, [FromQuery] int take = 200, CancellationToken cancellationToken = default) =>
        ApiResponse<IReadOnlyList<DiagnosticIncidentSummaryDto>>.Ok(await diagnostics.ListIncidentsAsync(threadId, q, take, cancellationToken));

    [HttpGet("incidents/{id}")]
    public async Task<ActionResult<ApiResponse<DiagnosticIncidentDto>>> Incident(string id, CancellationToken cancellationToken) =>
        await diagnostics.GetIncidentAsync(id, cancellationToken) is { } incident
            ? ApiResponse<DiagnosticIncidentDto>.Ok(incident)
            : throw NotFoundError();

    // A companion file as plain text; download=true saves it.
    [HttpGet("incidents/{id}/files/{name}")]
    public async Task<IActionResult> IncidentFile(string id, string name, [FromQuery] bool download = false, CancellationToken cancellationToken = default)
    {
        var text = await diagnostics.ReadIncidentFileAsync(id, name, cancellationToken) ?? throw NotFoundError();
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        return download ? File(bytes, "text/plain; charset=utf-8", name) : File(bytes, "text/plain; charset=utf-8");
    }

    [HttpDelete("incidents/{id}")]
    public ActionResult<ApiResponse<int>> Delete(string id) => ApiResponse<int>.Ok(diagnostics.DeleteIncidents(id));

    [HttpDelete("incidents")]
    public ActionResult<ApiResponse<int>> DeleteAll() => ApiResponse<int>.Ok(diagnostics.DeleteIncidents(null));

    [HttpGet("logs")]
    public ActionResult<ApiResponse<IReadOnlyList<LogFileDto>>> Logs() => ApiResponse<IReadOnlyList<LogFileDto>>.Ok(diagnostics.ListLogFiles());

    [HttpGet("logs/{name}")]
    public async Task<ActionResult<ApiResponse<LogFileTailDto>>> Log(string name, [FromQuery] int tail = 500, [FromQuery] string? q = null,
        CancellationToken cancellationToken = default) =>
        await diagnostics.ReadLogAsync(name, tail, q, cancellationToken) is { } log
            ? ApiResponse<LogFileTailDto>.Ok(log)
            : throw NotFoundError();

    // The ZIP is built in a temporary file (ZipArchive writes synchronously, which Kestrel refuses on the response).
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] DiagnosticExportRequest request, CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"ait-diagnostics-{Guid.NewGuid():N}.zip");
        var stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 81920, FileOptions.DeleteOnClose);
        try
        {
            var name = await diagnostics.ExportAsync(request, stream, cancellationToken);
            stream.Position = 0;
            return File(stream, "application/zip", name);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private static AppValidationException NotFoundError() =>
        new(Lang.T("Data log tidak ditemukan (mungkin sudah dihapus).", "Log data not found (it may have been deleted)."));
}
