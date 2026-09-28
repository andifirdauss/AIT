using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(IProjectService projectService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectDto>>>> GetAll(CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<ProjectDto>>.Ok(await projectService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ProjectDetailDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        await projectService.GetDetailAsync(id, cancellationToken) is { } detail
            ? ApiResponse<ProjectDetailDto>.Ok(detail)
            : NotFound(ApiResponse<ProjectDetailDto>.Fail("Project tidak ditemukan."));

    // Registers a project manually from a .sln/.slnx path or its folder.
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProjectDto>>> Register(RegisterProjectRequest request, CancellationToken cancellationToken) =>
        ApiResponse<ProjectDto>.Ok(await projectService.RegisterAsync(request.Path, cancellationToken), "Project terdaftar.");

    // Registers several solution files picked in the browser or found by a scan.
    [HttpPost("batch")]
    public async Task<ActionResult<ApiResponse<RegisterProjectsResultDto>>> RegisterMany(RegisterProjectsRequest request, CancellationToken cancellationToken)
    {
        var result = await projectService.RegisterManyAsync(request.Paths, cancellationToken);
        return ApiResponse<RegisterProjectsResultDto>.Ok(result, $"{result.Registered} project terdaftar, {result.AlreadyRegistered} sudah terdaftar sebelumnya.");
    }

    // Lists the sub-folders and solution files of a folder; no path lists the drives.
    [HttpGet("browse")]
    public async Task<ActionResult<ApiResponse<FolderBrowseDto>>> Browse([FromQuery] string? path, CancellationToken cancellationToken) =>
        ApiResponse<FolderBrowseDto>.Ok(await projectService.BrowseAsync(path, cancellationToken));

    // Finds .sln/.slnx files below a folder, up to the given depth.
    [HttpGet("scan")]
    public async Task<ActionResult<ApiResponse<SolutionScanDto>>> Scan([FromQuery] string path, [FromQuery] int depth = 4, CancellationToken cancellationToken = default) =>
        ApiResponse<SolutionScanDto>.Ok(await projectService.ScanAsync(path, depth, cancellationToken));

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken) =>
        await projectService.DeleteAsync(id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, "Project dihapus.")
            : NotFound(ApiResponse<bool>.Fail("Project tidak ditemukan."));
}
