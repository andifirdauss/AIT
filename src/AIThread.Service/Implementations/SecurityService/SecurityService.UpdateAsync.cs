using AIThread.Common.DTOs;
using AIThread.Common.Localization;
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
            activityLog.Info("keamanan", Lang.T($"Penyamaran secret aktif{(request.PassthroughMasking ? ", termasuk request passthrough" : ", kecuali request passthrough")}.", $"Secret masking on{(request.PassthroughMasking ? ", including passthrough requests" : ", except passthrough requests")}."));
        else
            activityLog.Warn("keamanan", Lang.T("Penyamaran secret dimatikan: isi file dan prompt dikirim ke model apa adanya.", "Secret masking turned off: file contents and prompts are sent to the model as they are."));
        return ToDto(entity);
    }
}
