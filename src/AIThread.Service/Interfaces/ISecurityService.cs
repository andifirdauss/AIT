using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

// Setting → Keamanan & privasi and the security notice shown the first time AIT is opened.
public interface ISecurityService
{
    Task<SecurityStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<SecurityStatusDto> UpdateAsync(UpdateSecurityRequest request, CancellationToken cancellationToken = default);

    Task<SecurityStatusDto> AcknowledgeNoticeAsync(CancellationToken cancellationToken = default);

    // The values masked since AIT started plus those marked as not secret, for the whitelist in Setting.
    Task<DetectedSecretsDto> GetSecretsAsync(CancellationToken cancellationToken = default);

    // Marks a masked value as not secret on this machine (false: AIT no longer knows the id), or masks it again.
    Task<bool> AllowSecretAsync(string id, CancellationToken cancellationToken = default);

    Task<bool> DisallowSecretAsync(string id, CancellationToken cancellationToken = default);

    // The real value behind an id while AIT still holds it (null otherwise).
    SecretValueDto? RevealSecret(string id);
}
