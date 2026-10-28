using AIThread.Service.Diagnostics;

namespace AIThread.Service.Implementations;

public sealed partial class DiagnosticsService
{
    public int DeleteIncidents(string? id)
    {
        var folders = id is null
            ? IncidentFolders.Load(IncidentsRoot).Select(x => x.Path).ToList()
            : IncidentFolders.FolderOf(IncidentsRoot, id) is { } one ? [one] : [];
        var deleted = 0;
        lock (_gate)
        {
            foreach (var folder in folders)
            {
                try
                {
                    Directory.Delete(folder, recursive: true);
                    deleted++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
            _recent.Clear();
        }
        return deleted;
    }
}
