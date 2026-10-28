using System.Text;
using AIThread.Common.DTOs;
using AIThread.Service.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Implementations;

public sealed partial class DiagnosticsService
{
    private const int MaxActivityRows = 20_000;

    public async Task<string> ExportAsync(DiagnosticExportRequest request, Stream output, CancellationToken cancellationToken = default)
    {
        var activity = request.Activity ? await ActivityTextAsync(request, cancellationToken) : null;
        await DiagnosticBundle.WriteAsync(output, new DiagnosticBundleInput(IncidentsRoot, LogRoot, request.Days, request.Incidents, request.RunLogs,
            request.ThreadId, activity, "web"), cancellationToken);
        return DiagnosticBundle.FileName(DateTime.Now);
    }

    // One line per entry, oldest first: "2026-10-28 19:01:36 Error [thread] (thread …) message".
    private async Task<string?> ActivityTextAsync(DiagnosticExportRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var since = DateTime.UtcNow.AddDays(-Math.Clamp(request.Days, 1, 90));
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            var query = db.ActivityLogs.AsNoTracking().Where(l => l.CreatedAt >= since);
            if (request.ThreadId is { } thread) query = query.Where(l => l.ThreadId == thread);
            var rows = await query.OrderByDescending(l => l.Id).Take(MaxActivityRows).ToListAsync(cancellationToken);

            var builder = new StringBuilder();
            foreach (var row in rows.AsEnumerable().Reverse())
            {
                builder.Append(row.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")).Append(' ').Append(row.Level).Append(" [").Append(row.Category).Append(']');
                if (row.ThreadId is { } id) builder.Append(" (thread ").Append(id.ToString("N")[..8]).Append(')');
                builder.Append(' ').Append(row.Message.Replace("\n", "\n    ")).Append('\n');
            }
            return builder.ToString();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The database is what failed: the ZIP still carries the run logs and the incidents.
            return $"(activity log not available: {ex.GetType().Name}: {ex.Message})\n";
        }
    }
}
