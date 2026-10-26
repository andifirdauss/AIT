using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    public async Task RejectAsync(Guid logId, string decidedBy, CancellationToken cancellationToken)
    {
        await using var catalog = await longStore.CreateCatalogAsync(cancellationToken);
        var log = await catalog.ModuleMergeLogs.FirstOrDefaultAsync(l => l.Id == logId, cancellationToken)
                  ?? throw new AppValidationException(Lang.T("Saran merge tidak ditemukan.", "Merge suggestion not found."));
        if (log.Status != ModuleMergeStatus.Suggested) throw new AppValidationException(Lang.T("Saran ini sudah diputuskan.", "This suggestion has already been decided."));
        log.Status = ModuleMergeStatus.Rejected;
        log.DecidedBy = Clip(decidedBy, 200);
        log.DecidedAt = DateTime.UtcNow;
        await catalog.SaveChangesAsync(cancellationToken);
        Publish("rejected", log.ProjectKey);
    }
}
