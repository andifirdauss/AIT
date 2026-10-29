using AIThread.Service.Diagnostics;

namespace AIThread.Service.Implementations;

public sealed partial class DiagnosticsService
{
    public async Task<string?> ReadIncidentFileAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        if (IncidentFolders.FolderOf(IncidentsRoot, id) is not { } folder) return null;
        // Only a file listed in the folder, by its exact name: no path reaches the disk.
        var file = IncidentFolders.Files(folder).FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.Ordinal));
        return file is null ? null : await File.ReadAllTextAsync(file.FullName, cancellationToken);
    }
}
