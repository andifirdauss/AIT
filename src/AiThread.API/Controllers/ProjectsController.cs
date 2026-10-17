using AiThread.Common.DTOs;
using AiThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(IProjectService projectService, IMemoryCatalogService catalog, ICodeStructureService structure) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectDto>>>> GetAll(CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<ProjectDto>>.Ok(await projectService.GetAllAsync(cancellationToken));

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

    // Module and layer of the project's files (the first layer of memory labels).
    [HttpGet("{id:guid}/codemap")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<CodeMapEntryDto>>>> CodeMap(Guid id, [FromQuery] string? search, [FromQuery] string? module,
        CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<CodeMapEntryDto>>.Ok(await catalog.GetCodeMapAsync(id, search, module, cancellationToken));

    // A manual entry; never overwritten by the rules or the model.
    [HttpPut("{id:guid}/codemap")]
    public async Task<ActionResult<ApiResponse<CodeMapEntryDto>>> SetCodeMap(Guid id, SetCodeMapRequest request, CancellationToken cancellationToken)
    {
        var entry = await catalog.SetCodeMapAsync(id, request, cancellationToken);
        return ApiResponse<CodeMapEntryDto>.Ok(entry, $"{entry.Path} → {entry.Module} · {entry.Layer}.");
    }

    // Maps every source file that has no entry yet.
    [HttpPost("{id:guid}/codemap/scan")]
    public async Task<ActionResult<ApiResponse<CodeMapScanResultDto>>> ScanCodeMap(Guid id, CancellationToken cancellationToken)
    {
        var result = await catalog.ScanCodeMapAsync(id, cancellationToken);
        return ApiResponse<CodeMapScanResultDto>.Ok(result, $"{result.Mapped} dari {result.Files} file terpetakan ke {result.Modules} modul.");
    }

    // Folder and class tree of the project (Struktur kode): counts of the latest walk; rescan=true walks again now.
    [HttpGet("{id:guid}/structure")]
    public async Task<ActionResult<ApiResponse<CodeStructureSummaryDto>>> Structure(Guid id, [FromQuery] bool rescan = false, CancellationToken cancellationToken = default) =>
        ApiResponse<CodeStructureSummaryDto>.Ok(await structure.GetSummaryAsync(id, rescan, cancellationToken));

    [HttpGet("{id:guid}/structure/folder")]
    public async Task<ActionResult<ApiResponse<StructureFolderDto>>> StructureFolder(Guid id, [FromQuery] string? path, CancellationToken cancellationToken) =>
        ApiResponse<StructureFolderDto>.Ok(await structure.GetFolderAsync(id, path, cancellationToken));

    [HttpGet("{id:guid}/structure/file")]
    public async Task<ActionResult<ApiResponse<StructureFileDto>>> StructureFile(Guid id, [FromQuery] string path, CancellationToken cancellationToken) =>
        ApiResponse<StructureFileDto>.Ok(await structure.GetFileAsync(id, path, cancellationToken));

    // Class view: namespaces (or folders, for files without one), the types in one, and one type with its members.
    [HttpGet("{id:guid}/structure/groups")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StructureGroupDto>>>> StructureGroups(Guid id, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<StructureGroupDto>>.Ok(await structure.GetGroupsAsync(id, cancellationToken));

    [HttpGet("{id:guid}/structure/types")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StructureTypeSummaryDto>>>> StructureTypes(Guid id, [FromQuery] string group, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<StructureTypeSummaryDto>>.Ok(await structure.GetTypesAsync(id, group, cancellationToken));

    [HttpGet("{id:guid}/structure/class")]
    public async Task<ActionResult<ApiResponse<StructureClassDto>>> StructureClass(Guid id, [FromQuery] string group, [FromQuery] string name,
        [FromQuery] string? container, CancellationToken cancellationToken) =>
        ApiResponse<StructureClassDto>.Ok(await structure.GetClassAsync(id, group, name, container, cancellationToken));

    [HttpGet("{id:guid}/structure/search")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StructureSearchHitDto>>>> StructureSearch(Guid id, [FromQuery] string q, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<StructureSearchHitDto>>.Ok(await structure.SearchAsync(id, q, cancellationToken));

    // Memory in a shared (external) store is kept for the other developers unless includeSharedMemory=true.
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, [FromQuery] bool includeSharedMemory = false, CancellationToken cancellationToken = default) =>
        await projectService.DeleteAsync(id, includeSharedMemory, cancellationToken)
            ? ApiResponse<bool>.Ok(true, "Project dihapus.")
            : NotFound(ApiResponse<bool>.Fail("Project tidak ditemukan."));
}
