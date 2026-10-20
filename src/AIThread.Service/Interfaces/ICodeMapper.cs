using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Memory;
using AIThread.Service.Models;

namespace AIThread.Service.Interfaces;

public interface ICodeMapper
{
    Task<(IReadOnlyList<CodeMapHit> Hits, IReadOnlyList<string> Unsure)> QuickAsync(ProjectContext project, IEnumerable<string> paths, CancellationToken cancellationToken);

    Task<IReadOnlyList<CodeMapHit>> MapAsync(ProjectContext project, IEnumerable<string> paths, CancellationToken cancellationToken);

    Task<IReadOnlyList<CodeMapHit>> MapQuicklyAsync(ProjectContext project, IEnumerable<string> paths, CancellationToken cancellationToken);

    Task<CodeMapHit> SetManualAsync(ProjectContext project, string path, string module, MemoryLayer? layer, CancellationToken cancellationToken);

    Task<(int Files, int Mapped)> ScanAsync(ProjectContext project, int maxFiles, CancellationToken cancellationToken, Action<int, int>? progress = null);

    Task<(int Files, int Mapped)> MapReferencedAsync(ProjectContext project, IEnumerable<string?> texts, CancellationToken cancellationToken, Action<int, int>? progress = null);

    Task<IReadOnlyList<string>> ResolveReferencesAsync(ProjectContext project, string? text, CancellationToken cancellationToken);

    Task<IReadOnlyList<CodeMapEntry>> LookupAsync(string key, string? text, int max, CancellationToken cancellationToken);

    Task<IReadOnlyList<CodeMapEntry>> ListAsync(string key, string? search, string? module, int take, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, CodeMapEntry>> StoredAsync(string key, IReadOnlyCollection<string> paths, CancellationToken cancellationToken);
}
