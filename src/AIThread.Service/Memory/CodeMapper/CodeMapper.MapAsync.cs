using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Every file mapped: the unsure ones by the model (or, without a model, by their name stem).
    public async Task<IReadOnlyList<CodeMapHit>> MapAsync(ProjectContext project, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var (hits, unsure) = await QuickAsync(project, paths, cancellationToken);
        if (unsure.Count == 0) return hits;

        await MapUnsureAsync(project, unsure, cancellationToken);
        var stored = await StoredAsync(project.Key, unsure, cancellationToken);
        return hits.Concat(stored.Values.Select(ToHit)).ToList();
    }
}
