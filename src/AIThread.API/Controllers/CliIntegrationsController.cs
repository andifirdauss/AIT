using AIThread.API.Infrastructure;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// The CLI menu: which coding CLIs can be pointed at this proxy, and the on/off toggle of each (globally or per project).
[ApiController]
[Route("api/cli")]
public class CliIntegrationsController(IEnumerable<ICliIntegration> integrations, IActivityLogService activityLog) : ControllerBase
{
    [HttpGet]
    public ActionResult<ApiResponse<CliListDto>> List() =>
        ApiResponse<CliListDto>.Ok(new CliListDto(ProxyUrlResolver.Resolve(HttpContext), integrations.Select(i => i.Info).ToList()));

    [HttpGet("{id}/status")]
    public async Task<ActionResult<ApiResponse<CliStatusDto>>> Status(
        string id, [FromQuery] CliScope scope, [FromQuery] Guid? projectId, [FromQuery] string? proxyBaseUrl, CancellationToken cancellationToken)
    {
        if (Find(id) is not { } cli) return NotFound(ApiResponse<CliStatusDto>.Fail("CLI tidak dikenal."));
        return ApiResponse<CliStatusDto>.Ok(await cli.GetStatusAsync(scope, projectId, ProxyUrl(proxyBaseUrl), cancellationToken));
    }

    [HttpGet("{id}/projects")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectCliStatusDto>>>> Projects(
        string id, [FromQuery] string? proxyBaseUrl, CancellationToken cancellationToken)
    {
        if (Find(id) is not { } cli) return NotFound(ApiResponse<IReadOnlyList<ProjectCliStatusDto>>.Fail("CLI tidak dikenal."));
        return ApiResponse<IReadOnlyList<ProjectCliStatusDto>>.Ok(await cli.GetProjectStatusesAsync(ProxyUrl(proxyBaseUrl), cancellationToken));
    }

    [HttpPut("{id}/toggle")]
    public async Task<ActionResult<ApiResponse<CliStatusDto>>> Toggle(string id, CliToggleRequest request, CancellationToken cancellationToken)
    {
        if (Find(id) is not { } cli) return NotFound(ApiResponse<CliStatusDto>.Fail("CLI tidak dikenal."));

        var proxyBaseUrl = ProxyUrl(request.ProxyBaseUrl);
        if (request.Enabled)
        {
            var applied = await cli.ApplyAsync(request.Scope, request.ProjectId, proxyBaseUrl, cancellationToken);
            activityLog.Success("setting", $"{cli.Info.Name} diarahkan ke AIT ({applied.Path}).");
            return ApiResponse<CliStatusDto>.Ok(applied, $"{cli.Info.Name} diarahkan ke AIT. {cli.Info.Note}");
        }

        var reverted = await cli.RevertAsync(request.Scope, request.ProjectId, proxyBaseUrl, cancellationToken);
        activityLog.Info("setting", $"Pengarahan {cli.Info.Name} ke AIT dihapus ({reverted.Path}).");
        return ApiResponse<CliStatusDto>.Ok(reverted, $"{cli.Info.Name} tidak lagi diarahkan ke AIT. Buka sesi baru agar berlaku.");
    }

    private ICliIntegration? Find(string id) =>
        integrations.FirstOrDefault(i => string.Equals(i.Info.Id, id, StringComparison.OrdinalIgnoreCase));

    private string ProxyUrl(string? requested) =>
        string.IsNullOrWhiteSpace(requested) ? ProxyUrlResolver.Resolve(HttpContext) : requested.Trim();
}
