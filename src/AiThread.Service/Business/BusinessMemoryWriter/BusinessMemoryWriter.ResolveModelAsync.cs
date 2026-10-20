using AiThread.Common.Enums;
using AiThread.Service.Models;

namespace AiThread.Service.Business;

public sealed partial class BusinessMemoryWriter
{
    public async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.BusinessMemoryModel)) return settings.BusinessMemoryModel;
        return (await nodeService.GetActiveAsync(NodeRole.Planner, cancellationToken)).FirstOrDefault()?.Model
               ?? throw new AppValidationException("Pilih model business memory di Setting → Business memory (atau tambahkan node Planner aktif).");
    }
}
