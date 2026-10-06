using AiThread.API.Infrastructure;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(
    IProjectService projectService,
    IClaudeSettingsService claudeSettings,
    IActivityLogService activityLog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectDto>>>> GetAll(CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<ProjectDto>>.Ok(await projectService.GetAllAsync(cancellationToken));

    // Whether each project's .claude/settings.local.json routes Claude Code through AiThread.
    [HttpGet("claude-integration")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectClaudeIntegrationDto>>>> ClaudeIntegration(CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<ProjectClaudeIntegrationDto>>.Ok(
            await claudeSettings.GetProjectStatusesAsync(ProxyUrlResolver.Resolve(HttpContext), cancellationToken));

    // Turns AiThread on/off for one project by writing or removing the env block in its settings.local.json.
    [HttpPut("{id:guid}/claude-integration")]
    public async Task<ActionResult<ApiResponse<ClaudeSettingsStatusDto>>> SetClaudeIntegration(
        Guid id, SetProjectClaudeIntegrationRequest request, CancellationToken cancellationToken)
    {
        var proxyBaseUrl = ProxyUrlResolver.Resolve(HttpContext);
        if (request.Enabled)
        {
            var applied = await claudeSettings.ApplyAsync(new ClaudeSettingsApplyRequest(ClaudeSettingsScope.Project, id, proxyBaseUrl, null), cancellationToken);
            activityLog.Success("setting", $"Claude Code diarahkan ke AiThread ({applied.Path}).");
            return ApiResponse<ClaudeSettingsStatusDto>.Ok(applied, "AiThread aktif untuk project ini. Buka sesi Claude Code baru di project ini agar berlaku.");
        }

        var reverted = await claudeSettings.RevertAsync(ClaudeSettingsScope.Project, id, proxyBaseUrl, cancellationToken);
        activityLog.Info("setting", $"Pengarahan Claude Code ke AiThread dihapus ({reverted.Path}).");
        return ApiResponse<ClaudeSettingsStatusDto>.Ok(reverted, "AiThread nonaktif untuk project ini. Buka sesi Claude Code baru agar berlaku.");
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ProjectDetailDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        await projectService.GetDetailAsync(id, cancellationToken) is { } detail
            ? ApiResponse<ProjectDetailDto>.Ok(detail)
            : NotFound(ApiResponse<ProjectDetailDto>.Fail("Project tidak ditemukan."));

    // Registers a project manually from its folder (a path inside it also works; a file path uses its folder).
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProjectDto>>> Register(RegisterProjectRequest request, CancellationToken cancellationToken) =>
        ApiResponse<ProjectDto>.Ok(await projectService.RegisterAsync(request.Path, cancellationToken), "Project terdaftar.");

    // Registers several project folders picked in the browser or found by a scan.
    [HttpPost("batch")]
    public async Task<ActionResult<ApiResponse<RegisterProjectsResultDto>>> RegisterMany(RegisterProjectsRequest request, CancellationToken cancellationToken)
    {
        var result = await projectService.RegisterManyAsync(request.Paths, cancellationToken);
        return ApiResponse<RegisterProjectsResultDto>.Ok(result, $"{result.Registered} project terdaftar, {result.AlreadyRegistered} sudah terdaftar sebelumnya.");
    }

    // Lists the sub-folders of a folder (marking project folders); no path lists the drives.
    [HttpGet("browse")]
    public async Task<ActionResult<ApiResponse<FolderBrowseDto>>> Browse([FromQuery] string? path, CancellationToken cancellationToken) =>
        ApiResponse<FolderBrowseDto>.Ok(await projectService.BrowseAsync(path, cancellationToken));

    // Finds project folders below a folder, up to the given depth.
    [HttpGet("scan")]
    public async Task<ActionResult<ApiResponse<ProjectScanDto>>> Scan([FromQuery] string path, [FromQuery] int depth = 4, CancellationToken cancellationToken = default) =>
        ApiResponse<ProjectScanDto>.Ok(await projectService.ScanAsync(path, depth, cancellationToken));

    // Sets the key that identifies the project across machines (memory is shared by it); an empty key clears it.
    [HttpPut("{id:guid}/key")]
    public async Task<ActionResult<ApiResponse<ProjectDto>>> SetKey(Guid id, SetProjectKeyRequest request, CancellationToken cancellationToken) =>
        await projectService.SetKeyAsync(id, request.Key, cancellationToken) is { } project
            ? ApiResponse<ProjectDto>.Ok(project, $"Project key: {project.Key}.")
            : NotFound(ApiResponse<ProjectDto>.Fail("Project tidak ditemukan."));

    // Memory in a shared (external) store is kept for the other developers unless includeSharedMemory=true.
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, [FromQuery] bool includeSharedMemory = false, CancellationToken cancellationToken = default) =>
        await projectService.DeleteAsync(id, includeSharedMemory, cancellationToken)
            ? ApiResponse<bool>.Ok(true, "Project dihapus.")
            : NotFound(ApiResponse<bool>.Fail("Project tidak ditemukan."));
}
