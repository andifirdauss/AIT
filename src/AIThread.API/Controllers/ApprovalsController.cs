using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Approvals;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Project → Detail → AIT permissions: the standing permissions ("allow always") of a project on this machine.
[ApiController]
[Route("api/projects/{projectId:guid}/approvals")]
public class ApprovalsController(IApprovalService approvals) : ControllerBase
{
    // ApprovalGrant.Target column length.
    private const int MaxTargetChars = 800;

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ApprovalGrantDto>>>> List(Guid projectId, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<ApprovalGrantDto>>.Ok(await approvals.ListAsync(projectId, cancellationToken));

    // A command permission added by hand: the exact command, or a pattern ("npm install *") checked like the one AIT offers.
    [HttpPost]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ApprovalGrantDto>>>> Add(Guid projectId, AddApprovalRequest request, CancellationToken cancellationToken)
    {
        if (request.Kind != ApprovalKind.Command)
            throw new AppValidationException(Lang.T("Hanya izin perintah yang bisa ditambahkan di sini.", "Only command permissions can be added here."));
        var target = request.Target?.Trim() ?? string.Empty;
        if (target.Length == 0 || target.Length > MaxTargetChars)
            throw new AppValidationException(Lang.T($"Tulis perintah atau pola (maks {MaxTargetChars} karakter).", $"Write a command or a pattern (at most {MaxTargetChars} characters)."));
        if (ApprovalTargets.IsPattern(target) && ApprovalTargets.PatternError(target) is { } error) throw new AppValidationException(error);

        await approvals.GrantAsync(projectId, request.Kind, target, cancellationToken: cancellationToken);
        return ApiResponse<IReadOnlyList<ApprovalGrantDto>>.Ok(await approvals.ListAsync(projectId, cancellationToken), Lang.T("Izin disimpan.", "Permission saved."));
    }

    [HttpDelete("{grantId:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Revoke(Guid projectId, Guid grantId, CancellationToken cancellationToken) =>
        await approvals.RevokeAsync(projectId, grantId, cancellationToken)
            ? ApiResponse<bool>.Ok(true, Lang.T("Izin dicabut.", "Permission revoked."))
            : NotFound(ApiResponse<bool>.Fail(Lang.T("Izin tidak ditemukan.", "Permission not found.")));
}
