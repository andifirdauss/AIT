using AiThread.Common.DTOs;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // Only the providers whose models the active nodes (and the fallbacks they hand calls to) use. Best effort: the
    // footer goes out without it when 9router's dashboard API does not answer.
    private async Task<IReadOnlyList<RouterQuotaDto>> NodeQuotasAsync(CancellationToken cancellationToken)
    {
        try
        {
            var nodes = await nodeService.GetAllAsync(cancellationToken);
            var byId = nodes.ToDictionary(n => n.Id);
            var used = new HashSet<Guid>();
            foreach (var node in nodes.Where(n => n.IsActive))
                for (NodeDto? current = node; current is not null && used.Add(current.Id);)
                    current = current.FallbackNodeId is { } next ? byId.GetValueOrDefault(next) : null;

            var models = nodes.Where(n => used.Contains(n.Id)).Select(n => n.Model.Trim()).Where(m => m.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return await router.GetQuotasAsync(models, cancellationToken);
        }
        // Also a client hanging up: the thread is already finished and must not turn into a cancelled one here.
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Provider quota for the footer unavailable");
            return [];
        }
    }
}
