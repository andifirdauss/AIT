using AIThread.Common.Options;
using AIThread.Data.Context;
using AIThread.Data.Messaging;
using AIThread.Service.Diagnostics;
using AIThread.Service.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AIThread.Service.Implementations;

// Incidents live on disk (IncidentFolders) so the export works without the database; the activity log of the export
// comes from the database when it is there. No ILogger here: DiagnosticsLoggerProvider feeds this service, and a
// failure while recording must not log (and so record) again.
public sealed partial class DiagnosticsService(
    IDbContextFactory<AIThreadDbContext> dbFactory,
    IEventBus eventBus,
    IOptions<AIThreadOptions> options) : IDiagnosticsService
{
    // The same error again within this window is folded into the earlier incident.
    private static readonly TimeSpan FoldWindow = TimeSpan.FromMinutes(10);
    private const int KeepIncidents = 300;
    private const int KeepDays = 30;
    private const int MaxAttachmentChars = 400_000;

    private readonly object _gate = new();
    private readonly Dictionary<string, (string Id, DateTime At)> _recent = new(StringComparer.Ordinal);
    private int _sinceprune;

    public string IncidentsRoot { get; } = IncidentFolders.Resolve(options.Value.DiagnosticsDirectory, "diagnostics");

    public string LogRoot { get; } = IncidentFolders.Resolve(options.Value.LogDirectory, "logs");
}
