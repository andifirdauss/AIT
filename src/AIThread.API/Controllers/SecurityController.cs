using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// Setting → Keamanan & privasi, and the security & privacy notice of the first visit (site.js).
[ApiController]
[Route("api/security")]
public class SecurityController(ISecurityService security) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<SecurityStatusDto>>> Get(CancellationToken cancellationToken) =>
        ApiResponse<SecurityStatusDto>.Ok(await security.GetStatusAsync(cancellationToken));

    [HttpPut]
    public async Task<ActionResult<ApiResponse<SecurityStatusDto>>> Update(UpdateSecurityRequest request, CancellationToken cancellationToken) =>
        ApiResponse<SecurityStatusDto>.Ok(await security.UpdateAsync(request, cancellationToken), Lang.T("Setting keamanan tersimpan.", "Security settings saved."));

    [HttpPost("acknowledge")]
    public async Task<ActionResult<ApiResponse<SecurityStatusDto>>> Acknowledge(CancellationToken cancellationToken) =>
        ApiResponse<SecurityStatusDto>.Ok(await security.AcknowledgeNoticeAsync(cancellationToken));
}
