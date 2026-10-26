using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;

namespace AIThread.Service.Business;

public sealed partial class BusinessMemoryWriter
{
    public async Task<string> ResolveModelAsync(CancellationToken cancellationToken)
    {
        var settings = await settingService.GetRuntimeAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.BusinessMemoryModel)) return settings.BusinessMemoryModel;
        return (await nodeService.GetActiveAsync(NodeRole.Planner, cancellationToken)).FirstOrDefault()?.Model
               ?? throw new AppValidationException(Lang.T("Pilih model business memory di Pengaturan → Business memory (atau tambahkan node Planner aktif).", "Choose the business memory model in Setting → Business memory (or add an active Planner node)."));
    }
}
