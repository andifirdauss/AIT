using System.Net;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Target of the Claude Code plugin's hook (integrations/claude-plugin): may this folder's code changes go through AIT?
// Answers only on this machine, like the other hook targets.
[ApiController]
[Route("api/skill")]
public class SkillGateController(ISkillGateService gate) : ControllerBase
{
    [HttpGet("gate")]
    public async Task<ActionResult<ApiResponse<SkillGateDto>>> Gate([FromQuery] string? cwd, CancellationToken cancellationToken)
    {
        if (HttpContext.Connection.RemoteIpAddress is { } address && !IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<SkillGateDto>.Fail(Lang.T("Hanya bisa dari komputer ini.", "Only available from this computer.")));
        if (string.IsNullOrWhiteSpace(cwd)) throw new AppValidationException(Lang.T("cwd wajib diisi.", "cwd is required."));

        return ApiResponse<SkillGateDto>.Ok(await gate.GetAsync(cwd, cancellationToken));
    }
}
