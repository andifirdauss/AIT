using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

// Setting → Keamanan & privasi and the security notice shown the first time AIT is opened.
public interface ISecurityService
{
    Task<SecurityStatusDto> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<SecurityStatusDto> UpdateAsync(UpdateSecurityRequest request, CancellationToken cancellationToken = default);

    Task<SecurityStatusDto> AcknowledgeNoticeAsync(CancellationToken cancellationToken = default);
}
