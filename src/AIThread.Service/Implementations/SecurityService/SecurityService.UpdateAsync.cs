using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;

namespace AIThread.Service.Implementations;

public sealed partial class SecurityService
{
    public async Task<SecurityStatusDto> UpdateAsync(UpdateSecurityRequest request, CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var entity = await LoadTrackedAsync(db, cancellationToken);
        entity.SecretMaskingEnabled = request.MaskingEnabled;
        entity.PassthroughSecretMasking = request.PassthroughMasking;
        entity.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        secrets.Configure(entity.SecretMaskingEnabled, entity.PassthroughSecretMasking);
        if (request.MaskingEnabled)
            activityLog.Info("keamanan", $"Penyamaran secret aktif{(request.PassthroughMasking ? ", termasuk request passthrough" : ", kecuali request passthrough")}.");
        else
            activityLog.Warn("keamanan", "Penyamaran secret dimatikan: isi file dan prompt dikirim ke model apa adanya.");
        return ToDto(entity);
    }
}
