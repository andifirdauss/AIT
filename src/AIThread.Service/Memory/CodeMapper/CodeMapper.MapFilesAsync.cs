using AIThread.Service.Models;

namespace AIThread.Service.Memory;

public sealed partial class CodeMapper
{
    private async Task<(int Files, int Mapped)> MapFilesAsync(ProjectContext project, IReadOnlyList<string> files, Action<int, int>? progress,
        CancellationToken cancellationToken)
    {
        var (hits, unsure) = await QuickAsync(project, files, cancellationToken);
        progress?.Invoke(hits.Count, files.Count);
        if (unsure.Count == 0) return (files.Count, hits.Count);

        await MapUnsureAsync(project, unsure, cancellationToken, decided => progress?.Invoke(hits.Count + decided, files.Count));
        var stored = await StoredAsync(project.Key, unsure, cancellationToken);
        return (files.Count, hits.Count + stored.Count);
    }
}
