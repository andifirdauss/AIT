using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

[ApiController]
[Route("api/projects")]
public class ProjectsController(IProjectService projectService, IMemoryCatalogService catalog, ICodeStructureService structure,
    IGitChangeTracker gitChanges, IGitEnvironmentTracker environments) : ControllerBase
{
    // The project key's environments (dev, staging, prod, ...), shared by every developer, with what this clone knows.
    [HttpGet("{id:guid}/environments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectEnvironmentDto>>>> Environments(Guid id, CancellationToken cancellationToken) =>
        await environments.ListAsync(id, cancellationToken) is { } list
            ? ApiResponse<IReadOnlyList<ProjectEnvironmentDto>>.Ok(list)
            : NotFound(ApiResponse<IReadOnlyList<ProjectEnvironmentDto>>.Fail(Lang.T("Project tidak ditemukan.", "Project not found.")));

    [HttpPut("{id:guid}/environments")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectEnvironmentDto>>>> SaveEnvironments(Guid id, SaveProjectEnvironmentsRequest request,
        CancellationToken cancellationToken) =>
        await environments.SaveAsync(id, request.Environments ?? [], cancellationToken) is { } list
            ? ApiResponse<IReadOnlyList<ProjectEnvironmentDto>>.Ok(list, Lang.T("Environment disimpan.", "Environments saved."))
            : NotFound(ApiResponse<IReadOnlyList<ProjectEnvironmentDto>>.Fail(Lang.T("Project tidak ditemukan.", "Project not found.")));

    // Looks for the shared changes in each environment's ref of this clone now (after a fetch of its own).
    [HttpPost("{id:guid}/environments/refresh")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectEnvironmentDto>>>> RefreshEnvironments(Guid id, CancellationToken cancellationToken)
    {
        var found = await environments.RefreshAsync(id, cancellationToken);
        return await environments.ListAsync(id, cancellationToken) is { } list
            ? ApiResponse<IReadOnlyList<ProjectEnvironmentDto>>.Ok(list, Lang.T($"{found} perubahan baru terlihat di environment.", $"{found} new change(s) seen in the environments."))
            : NotFound(ApiResponse<IReadOnlyList<ProjectEnvironmentDto>>.Fail(Lang.T("Project tidak ditemukan.", "Project not found.")));
    }

    // Asks git again where the change of every followed thread of the project is now; the count of threads whose state
    // changed. The UI hears each change over SSE (thread.git).
    [HttpPost("{id:guid}/git/refresh")]
    public async Task<ActionResult<ApiResponse<int>>> RefreshGit(Guid id, CancellationToken cancellationToken)
    {
        var changed = await gitChanges.RefreshProjectAsync(id, cancellationToken);
        return ApiResponse<int>.Ok(changed, Lang.T($"Status git disinkronkan: {changed} thread berubah.", $"Git state synced: {changed} thread(s) changed."));
    }

    // Git of the project: automatic fetch and its last result, threads followed.
    [HttpGet("{id:guid}/git")]
    public async Task<ActionResult<ApiResponse<ProjectGitDto>>> Git(Guid id, CancellationToken cancellationToken) =>
        await gitChanges.GetProjectAsync(id, cancellationToken) is { } git
            ? ApiResponse<ProjectGitDto>.Ok(git)
            : NotFound(ApiResponse<ProjectGitDto>.Fail(Lang.T("Project tidak ditemukan.", "Project not found.")));

    [HttpPut("{id:guid}/git")]
    public async Task<ActionResult<ApiResponse<ProjectGitDto>>> SetGit(Guid id, SetGitFetchRequest request, CancellationToken cancellationToken) =>
        await gitChanges.SetFetchMinutesAsync(id, request.FetchMinutes, cancellationToken) is { } git
            ? ApiResponse<ProjectGitDto>.Ok(git, Lang.T("Pengaturan git disimpan.", "Git settings saved."))
            : NotFound(ApiResponse<ProjectGitDto>.Fail(Lang.T("Project tidak ditemukan.", "Project not found.")));

    // git fetch of every remote now, then the followed threads are looked at again.
    [HttpPost("{id:guid}/git/fetch")]
    public async Task<ActionResult<ApiResponse<ProjectGitDto>>> FetchGit(Guid id, CancellationToken cancellationToken)
    {
        var status = await gitChanges.FetchAsync(id, cancellationToken);
        if (status is { Error: null }) await environments.RefreshAsync(id, cancellationToken);
        if (await gitChanges.GetProjectAsync(id, cancellationToken) is not { } git)
            return NotFound(ApiResponse<ProjectGitDto>.Fail(Lang.T("Project tidak ditemukan.", "Project not found.")));
        return status switch
        {
            null => ApiResponse<ProjectGitDto>.Ok(git, Lang.T("Tidak ada git remote untuk di-fetch.", "No git remote to fetch.")),
            { Error: { } error } => ApiResponse<ProjectGitDto>.Ok(git, Lang.T($"git fetch gagal: {error}", $"git fetch failed: {error}")),
            _ => ApiResponse<ProjectGitDto>.Ok(git, Lang.T("git fetch selesai.", "git fetch done."))
        };
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectDto>>>> GetAll(CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<ProjectDto>>.Ok(await projectService.GetAllAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<ProjectDetailDto>>> Get(Guid id, CancellationToken cancellationToken) =>
        await projectService.GetDetailAsync(id, cancellationToken) is { } detail
            ? ApiResponse<ProjectDetailDto>.Ok(detail)
            : NotFound(ApiResponse<ProjectDetailDto>.Fail(Lang.T("Project tidak ditemukan.", "Project not found.")));

    // Registers a project manually from its folder (a path inside it also works; a file path uses its folder).
    [HttpPost]
    public async Task<ActionResult<ApiResponse<ProjectDto>>> Register(RegisterProjectRequest request, CancellationToken cancellationToken) =>
        ApiResponse<ProjectDto>.Ok(await projectService.RegisterAsync(request.Path, cancellationToken), Lang.T("Project terdaftar.", "Project registered."));

    // Registers several project folders picked in the browser or found by a scan.
    [HttpPost("batch")]
    public async Task<ActionResult<ApiResponse<RegisterProjectsResultDto>>> RegisterMany(RegisterProjectsRequest request, CancellationToken cancellationToken)
    {
        var result = await projectService.RegisterManyAsync(request.Paths, cancellationToken);
        return ApiResponse<RegisterProjectsResultDto>.Ok(result, Lang.T($"{result.Registered} project terdaftar, {result.AlreadyRegistered} sudah terdaftar sebelumnya.", $"{result.Registered} project(s) registered, {result.AlreadyRegistered} already registered before."));
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
            : NotFound(ApiResponse<ProjectDto>.Fail(Lang.T("Project tidak ditemukan.", "Project not found.")));

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
        return ApiResponse<CodeMapScanResultDto>.Ok(result, Lang.T($"{result.Mapped} dari {result.Files} file terpetakan ke {result.Modules} modul.", $"{result.Mapped} of {result.Files} files mapped to {result.Modules} modules."));
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

    // What one function calls, as read by the tools node; key from a member of /structure/file or /structure/class.
    [HttpGet("{id:guid}/structure/calls")]
    public async Task<ActionResult<ApiResponse<StructureFunctionCallsDto>>> StructureCalls(Guid id, [FromQuery] string key, CancellationToken cancellationToken) =>
        await structure.GetCallsAsync(id, key, cancellationToken) is { } calls
            ? ApiResponse<StructureFunctionCallsDto>.Ok(calls)
            : NotFound(ApiResponse<StructureFunctionCallsDto>.Fail(Lang.T("Panggilan function ini belum pernah dibaca.", "The calls of this function have not been read yet.")));

    [HttpGet("{id:guid}/structure/search")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<StructureSearchHitDto>>>> StructureSearch(Guid id, [FromQuery] string q, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<StructureSearchHitDto>>.Ok(await structure.SearchAsync(id, q, cancellationToken));

    // Memory in a shared (external) store is kept for the other developers unless includeSharedMemory=true.
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(Guid id, [FromQuery] bool includeSharedMemory = false, CancellationToken cancellationToken = default) =>
        await projectService.DeleteAsync(id, includeSharedMemory, cancellationToken)
            ? ApiResponse<bool>.Ok(true, Lang.T("Project dihapus.", "Project deleted."))
            : NotFound(ApiResponse<bool>.Fail(Lang.T("Project tidak ditemukan.", "Project not found.")));
}
