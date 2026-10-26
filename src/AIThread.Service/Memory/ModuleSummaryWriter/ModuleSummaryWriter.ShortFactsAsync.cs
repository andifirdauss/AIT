using AIThread.Common.Enums;
using AIThread.Common.Memory;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Memory;

public sealed partial class ModuleSummaryWriter
{
    // Facts still in short memory: the same rows the migration takes once they are consolidated (labeled project facts
    // with a key).
    private async Task<int> ShortFactsAsync()
    {
        if (!shortStore.IsConnected) return 0;
        try
        {
            return (await shortStore.GetAllAsync()).Count(r => r.Type == MemoryType.Project && r.ProjectKey is not null && r.Module is not null
                && r.LabelVersion is not null && r.ReviewReason is null && MemoryLabels.IsFact(r.Kind));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not read short memories for the change log migration.");
            return 0;
        }
    }
}
