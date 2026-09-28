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

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, CancellationToken cancellationToken) =>
        await projectService.DeleteAsync(id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, "Project dihapus.")
            : NotFound(ApiResponse<bool>.Fail("Project tidak ditemukan."));
}
