using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Project → Detail → AIT permissions: the standing permissions ("allow always") of a project on this machine.
[ApiController]
[Route("api/projects/{projectId:guid}/approvals")]
public class ApprovalsController(IApprovalService approvals) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ApprovalGrantDto>>>> List(Guid projectId, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<ApprovalGrantDto>>.Ok(await approvals.ListAsync(projectId, cancellationToken));

    [HttpDelete("{grantId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Revoke(Guid projectId, Guid grantId, CancellationToken cancellationToken) =>
        await approvals.RevokeAsync(projectId, grantId, cancellationToken)
            ? ApiResponse<bool>.Ok(true, Lang.T("Izin dicabut.", "Permission revoked."))
            : NotFound(ApiResponse<bool>.Fail(Lang.T("Izin tidak ditemukan.", "Permission not found.")));
}
