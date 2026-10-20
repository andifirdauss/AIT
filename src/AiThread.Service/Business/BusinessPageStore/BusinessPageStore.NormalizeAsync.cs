using AiThread.Common.Memory;
using Microsoft.Extensions.Logging;

namespace AiThread.Service.Business;

public sealed partial class BusinessPageStore
{
    private async Task<BusinessPageDraft> NormalizeAsync(BusinessPageDraft draft, CancellationToken cancellationToken)
    {
        var module = MemoryLabels.Module(draft.Module) ?? draft.Module;
        try
        {
            (module, _) = await registry.EnsureAsync(draft.ProjectKey, module, draft.Summary, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The module list lives with long memory; while that store is down the name is kept as written.
            logger.LogWarning(ex, "Module {Module} of a business page could not be resolved.", module);
        }
        var labels = string.Join(", ", MemoryLabels.Keywords(MemoryLabels.SplitCsv(draft.Labels)));
        return draft with { Module = module, Labels = labels };
    }
}
