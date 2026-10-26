using AIThread.API.Infrastructure;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

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
        activityLog.Info("setting", Lang.T("Setting disimpan.", "Settings saved."));

        // Refresh the dashboard right away instead of waiting for the next monitor tick.
        _ = await router.CheckStatusAsync(cancellationToken);
        return ApiResponse<SystemSettingDto>.Ok(updated, Lang.T("Setting tersimpan.", "Settings saved."));
    }

    [HttpPut("language")]
    public async Task<ActionResult<ApiResponse<string>>> UpdateLanguage(UpdateLanguageRequest request, CancellationToken cancellationToken)
    {
        await settingService.UpdateLanguageAsync(request.Language, cancellationToken);
        return ApiResponse<string>.Ok(Lang.Current, Lang.T("Bahasa disimpan.", "Language saved."));
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
        activityLog.Success("setting", Lang.T($"Claude Code diarahkan ke AIT ({status.Path}).", $"Claude Code now points to AIT ({status.Path})."));
        return ApiResponse<ClaudeSettingsStatusDto>.Ok(status, Lang.T("Claude Code settings diperbarui. Buka sesi Claude Code baru agar berlaku.", "Claude Code settings updated. Open a new Claude Code session for them to apply."));
    }

    [HttpPost("claude/revert")]
    public async Task<ActionResult<ApiResponse<ClaudeSettingsStatusDto>>> RevertClaude(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken)
    {
        var status = await claudeSettings.RevertAsync(request.Scope, request.ProjectId, ProxyUrlResolver.Resolve(HttpContext), cancellationToken);
        activityLog.Info("setting", Lang.T($"Pengarahan Claude Code ke AIT dihapus ({status.Path}).", $"Claude Code no longer points to AIT ({status.Path})."));
        return ApiResponse<ClaudeSettingsStatusDto>.Ok(status, Lang.T("Env AIT dihapus dari settings Claude Code.", "AIT env removed from the Claude Code settings."));
    }

    [HttpGet("claude/hooks")]
    public async Task<ActionResult<ApiResponse<ClaudeHooksStatusDto>>> ClaudeHooksStatus(
        [FromQuery] ClaudeSettingsScope scope, [FromQuery] Guid? projectId, [FromQuery] string? baseUrl, CancellationToken cancellationToken) =>
        ApiResponse<ClaudeHooksStatusDto>.Ok(await claudeSettings.GetHooksStatusAsync(
            scope, projectId, string.IsNullOrWhiteSpace(baseUrl) ? ProxyUrlResolver.Resolve(HttpContext) : baseUrl, cancellationToken));

    [HttpPost("claude/hooks/apply")]
    public async Task<ActionResult<ApiResponse<ClaudeHooksStatusDto>>> ApplyClaudeHooks(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken)
    {
        var status = await claudeSettings.ApplyHooksAsync(request, cancellationToken);
        activityLog.Success("setting", Lang.T($"Hook /clear Context Keeper dipasang ({status.Path}).", $"Context Keeper /clear hook installed ({status.Path})."));
        return ApiResponse<ClaudeHooksStatusDto>.Ok(status, Lang.T("Hook /clear terpasang. Berlaku untuk sesi Claude Code yang dibuka setelah ini.", "/clear hook installed. It applies to Claude Code sessions opened after this."));
    }

    [HttpPost("claude/hooks/revert")]
    public async Task<ActionResult<ApiResponse<ClaudeHooksStatusDto>>> RevertClaudeHooks(ClaudeSettingsApplyRequest request, CancellationToken cancellationToken)
    {
        var status = await claudeSettings.RevertHooksAsync(request.Scope, request.ProjectId, request.ProxyBaseUrl, cancellationToken);
        activityLog.Info("setting", Lang.T($"Hook /clear Context Keeper dilepas ({status.Path}).", $"Context Keeper /clear hook removed ({status.Path})."));
        return ApiResponse<ClaudeHooksStatusDto>.Ok(status, Lang.T("Hook /clear AIT dihapus dari settings Claude Code.", "AIT /clear hook removed from the Claude Code settings."));
    }
}
