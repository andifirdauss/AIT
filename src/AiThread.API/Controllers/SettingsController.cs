using AiThread.API.Infrastructure;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

[ApiController]
[Route("api/settings")]
public class SettingsController(
    ISettingService settingService,
    INineRouterService router,
    IClaudeSettingsService claudeSettings,
    IActivityLogService activityLog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<SystemSettingDto>>> Get(CancellationToken cancellationToken) =>
        ApiResponse<SystemSettingDto>.Ok(await settingService.GetAsync(ProxyUrlResolver.Resolve(HttpContext), cancellationToken));

    [HttpPut]
    public async Task<ActionResult<ApiResponse<SystemSettingDto>>> Update(UpdateSystemSettingRequest request, CancellationToken cancellationToken)
    {
        var updated = await settingService.UpdateAsync(request, ProxyUrlResolver.Resolve(HttpContext), cancellationToken);
        activityLog.Info("setting", "Setting disimpan.");

        // Refresh the dashboard right away instead of waiting for the next monitor tick.
        _ = await router.CheckStatusAsync(cancellationToken);
        return ApiResponse<SystemSettingDto>.Ok(updated, "Setting tersimpan.");
    }

    [HttpPost("test-router")]
    public async Task<ActionResult<ApiResponse<RouterStatusDto>>> TestRouter(TestRouterRequest request, CancellationToken cancellationToken) =>
        ApiResponse<RouterStatusDto>.Ok(await router.TestConnectionAsync(request.BaseUrl, request.ApiKey, cancellationToken));

    [HttpGet("claude")]
    public async Task<ActionResult<ApiResponse<ClaudeSettingsStatusDto>>> ClaudeStatus(
        [FromQuery] ClaudeSettingsScope scope, [FromQuery] Guid? projectId, CancellationToken cancellationToken) =>
        ApiResponse<ClaudeSettingsStatusDto>.Ok(await claudeSettings.GetStatusAsync(scope, projectId, ProxyUrlResolver.Resolve(HttpContext), cancellationToken));

    [HttpPost("claude/apply")]
    public async Task<ActionResult<ApiResponse<ClaudeSettingsStatusDto>>> ApplyClaude(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken)
    {
        var status = await claudeSettings.ApplyAsync(request, cancellationToken);
        activityLog.Success("setting", $"Claude Code diarahkan ke AiThread ({status.Path}).");
        return ApiResponse<ClaudeSettingsStatusDto>.Ok(status, "Claude Code settings diperbarui. Buka sesi Claude Code baru agar berlaku.");
    }

    [HttpPost("claude/revert")]
    public async Task<ActionResult<ApiResponse<ClaudeSettingsStatusDto>>> RevertClaude(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken)
    {
        var status = await claudeSettings.RevertAsync(request.Scope, request.ProjectId, ProxyUrlResolver.Resolve(HttpContext), cancellationToken);
        activityLog.Info("setting", $"Pengarahan Claude Code ke AiThread dihapus ({status.Path}).");
        return ApiResponse<ClaudeSettingsStatusDto>.Ok(status, "Env AiThread dihapus dari settings Claude Code.");
    }
}
