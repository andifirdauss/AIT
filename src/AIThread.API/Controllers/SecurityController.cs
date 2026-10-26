using System.Net;
using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Http;
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
        ApiResponse<SecurityStatusDto>.Ok(await security.UpdateAsync(request, cancellationToken), Lang.T("Pengaturan keamanan tersimpan.", "Security settings saved."));

    [HttpPost("acknowledge")]
    public async Task<ActionResult<ApiResponse<SecurityStatusDto>>> Acknowledge(CancellationToken cancellationToken) =>
        ApiResponse<SecurityStatusDto>.Ok(await security.AcknowledgeNoticeAsync(cancellationToken));

    // The values masked since AIT started, and those marked as not secret (the whitelist).
    [HttpGet("secrets")]
    public async Task<ActionResult<ApiResponse<DetectedSecretsDto>>> Secrets(CancellationToken cancellationToken) =>
        ApiResponse<DetectedSecretsDto>.Ok(await security.GetSecretsAsync(cancellationToken));

    [HttpPost("secrets/{id:length(10)}/allow")]
    public async Task<ActionResult<ApiResponse<bool>>> Allow(string id, CancellationToken cancellationToken) =>
        await security.AllowSecretAsync(id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, Lang.T("Ditandai bukan secret: nilai ini tidak disamarkan lagi.", "Marked as not secret: this value is no longer masked."))
            : NotFound(ApiResponse<bool>.Fail(Lang.T("Nilai ini tidak dikenal lagi (AIT sudah di-restart); tunggu sampai disamarkan lagi.", "This value is no longer known (AIT restarted); wait until it is masked again.")));

    [HttpDelete("secrets/{id:length(10)}/allow")]
    public async Task<ActionResult<ApiResponse<bool>>> Disallow(string id, CancellationToken cancellationToken) =>
        await security.DisallowSecretAsync(id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, Lang.T("Nilai ini disamarkan lagi.", "This value is masked again."))
            : NotFound(ApiResponse<bool>.Fail(Lang.T("Nilai ini tidak ada di daftar bukan secret.", "This value is not in the not-secret list.")));

    // The real value, only for a browser on this computer: AIT can be reached from the network when it listens there.
    [HttpGet("secrets/{id:length(10)}/value")]
    public ActionResult<ApiResponse<SecretValueDto>> Value(string id)
    {
        if (HttpContext.Connection.RemoteIpAddress is { } remote && !IPAddress.IsLoopback(remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote))
            return StatusCode(StatusCodes.Status403Forbidden, ApiResponse<SecretValueDto>.Fail(Lang.T("Nilai asli hanya bisa dilihat dari komputer ini.", "The real value can only be seen from this computer.")));
        return security.RevealSecret(id) is { } value
            ? ApiResponse<SecretValueDto>.Ok(value)
            : NotFound(ApiResponse<SecretValueDto>.Fail(Lang.T("Nilai ini tidak lagi ada di memori AIT (AIT sudah di-restart).", "This value is no longer in AIT's memory (AIT restarted).")));
    }
}
