using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Interfaces;

// The developer's standing permissions ("allow always") per project on this machine: writes outside the project,
// commands, outside hosts, commit and push.
public interface IApprovalService
{
    // True when a saved grant covers the action; the grant's use is recorded (count, last used, activity log).
    Task<bool> UseAsync(Guid projectId, ApprovalKind kind, string target, Guid? threadId = null, CancellationToken cancellationToken = default);

    // Saves "allow always" for the target (nothing changes when a grant already covers it).
    Task GrantAsync(Guid projectId, ApprovalKind kind, string target, Guid? threadId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApprovalGrantDto>> ListAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<bool> RevokeAsync(Guid projectId, Guid grantId, CancellationToken cancellationToken = default);
}
