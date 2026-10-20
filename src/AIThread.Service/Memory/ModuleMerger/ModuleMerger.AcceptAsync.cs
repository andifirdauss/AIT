using AIThread.Common.Entities;
using AIThread.Common.Enums;
using AIThread.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Memory;

public sealed partial class ModuleMerger
{
    public async Task AcceptAsync(Guid logId, string decidedBy, CancellationToken cancellationToken)
    {
        ModuleMergeLog log;
        await using (var catalog = await longStore.CreateCatalogAsync(cancellationToken))
            log = await catalog.ModuleMergeLogs.AsNoTracking().FirstOrDefaultAsync(l => l.Id == logId, cancellationToken)
                  ?? throw new AppValidationException("Saran merge tidak ditemukan.");
        if (log.Status != ModuleMergeStatus.Suggested) throw new AppValidationException("Saran ini sudah diputuskan.");
        await MergeAsync(log.ProjectKey, log.FromModule, log.IntoModule, log.Reason, log.Confidence, decidedBy, cancellationToken, log.Id);
    }
}
