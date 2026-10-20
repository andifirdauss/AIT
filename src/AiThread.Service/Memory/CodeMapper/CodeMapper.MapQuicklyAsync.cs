using AiThread.Service.Models;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Memory;

public sealed partial class CodeMapper
{
    // Rules now, the model in the background for the rest (used before the planner, which must not wait for it).
    public async Task<IReadOnlyList<CodeMapHit>> MapQuicklyAsync(ProjectContext project, IEnumerable<string> paths, CancellationToken cancellationToken)
    {
        var (hits, unsure) = await QuickAsync(project, paths, cancellationToken);
        if (unsure.Count > 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await MapUnsureAsync(project, unsure, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Background code mapping of {Count} file(s) failed.", unsure.Count);
                }
            }, CancellationToken.None);
        }
        return hits;
    }
}
