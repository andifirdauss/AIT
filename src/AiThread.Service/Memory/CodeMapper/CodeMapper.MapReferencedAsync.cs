using AiThread.Service.Models;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    // The files the texts name (paths and identifiers, read the way LookupAsync reads them), mapped: all the code map
    // that labeling those texts needs, without scanning the whole project. progress as in ScanAsync.
    public async Task<(int Files, int Mapped)> MapReferencedAsync(ProjectContext project, IEnumerable<string?> texts, CancellationToken cancellationToken,
        Action<int, int>? progress = null)
    {
        if (string.IsNullOrEmpty(project.Key) || !Directory.Exists(project.RootPath)) return (0, 0);
        var files = FindReferenced(project, texts);
        return files.Count == 0 ? (0, 0) : await MapFilesAsync(project, files, progress, cancellationToken);
    }
}
