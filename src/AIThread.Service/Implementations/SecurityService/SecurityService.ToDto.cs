using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Implementations;

public sealed partial class SecurityService
{
    private SecurityStatusDto ToDto(SystemSetting entity)
    {
        var counters = secrets.GetCounters();
        return new SecurityStatusDto(
            entity.SecretMaskingEnabled,
            entity.PassthroughSecretMasking,
            entity.PrivacyNoticeAcknowledgedAt,
            counters.Masked,
            counters.KnownSecrets,
            counters.Restored,
            counters.BlockedReads);
    }
}
