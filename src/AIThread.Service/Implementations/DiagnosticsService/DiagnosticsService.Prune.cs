using AIThread.Service.Diagnostics;

namespace AIThread.Service.Implementations;

public sealed partial class DiagnosticsService
{
    // Keeps the newest KeepIncidents incidents of the last KeepDays days.
    private void Prune(DateTime now)
    {
        var index = 0;
        foreach (var (incident, folder) in IncidentFolders.Load(IncidentsRoot).ToList())
        {
            if (index++ < KeepIncidents && incident.LastAt >= now.AddDays(-KeepDays)) continue;
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
