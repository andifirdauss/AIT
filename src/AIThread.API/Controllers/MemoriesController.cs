using System.Text.Json;
using System.Text.Json.Serialization;
using AIThread.Common.DTOs;
using AIThread.Common.Enums;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

[ApiController]
[Route("api/memory")]
public class MemoriesController(IMemoryService memoryService, IMemoryTransferService transferService, IMemoryCatalogService catalog,
    IModuleSummaryWriter summaryWriter) : ControllerBase
{
    private static readonly JsonSerializerOptions ExportJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<MemoryItemDto>>>> Query([FromQuery] MemoryQuery query, CancellationToken cancellationToken) =>
        ApiResponse<PagedResult<MemoryItemDto>>.Ok(await memoryService.QueryAsync(query, cancellationToken));

    [HttpGet("stats")]
    public async Task<ActionResult<ApiResponse<MemoryStatsDto>>> Stats(CancellationToken cancellationToken) =>
        ApiResponse<MemoryStatsDto>.Ok(await memoryService.GetStatsAsync(cancellationToken));

    [HttpGet("{level}/{id:guid}")]
    public async Task<ActionResult<ApiResponse<MemoryItemDto>>> Get(MemoryLevel level, Guid id, CancellationToken cancellationToken) =>
        await memoryService.GetAsync(level, id, cancellationToken) is { } item
            ? ApiResponse<MemoryItemDto>.Ok(item)
            : NotFound(ApiResponse<MemoryItemDto>.Fail("Memory tidak ditemukan."));

    // Manual entries start as short memory, like everything the pipeline learns.
    [HttpPost]
    public async Task<ActionResult<ApiResponse<MemoryItemDto>>> Create(SaveMemoryRequest request, CancellationToken cancellationToken) =>
        ApiResponse<MemoryItemDto>.Ok(await memoryService.AddShortAsync(request, null, cancellationToken), "Memory ditambahkan.");

    [HttpPut("{level}/{id:guid}")]
    public async Task<ActionResult<ApiResponse<MemoryItemDto>>> Update(MemoryLevel level, Guid id, SaveMemoryRequest request, CancellationToken cancellationToken) =>
        await memoryService.UpdateAsync(level, id, request, cancellationToken) is { } item
            ? ApiResponse<MemoryItemDto>.Ok(item, "Memory diperbarui.")
            : NotFound(ApiResponse<MemoryItemDto>.Fail("Memory tidak ditemukan."));

    [HttpDelete("{level}/{id:guid}")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(MemoryLevel level, Guid id, CancellationToken cancellationToken) =>
        await memoryService.DeleteAsync(level, id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, "Memory dihapus.")
            : NotFound(ApiResponse<bool>.Fail("Memory tidak ditemukan."));

    [HttpPost("{id:guid}/promote")]
    public async Task<ActionResult<ApiResponse<bool>>> Promote(Guid id, CancellationToken cancellationToken) =>
        await memoryService.PromoteAsync(id, cancellationToken)
            ? ApiResponse<bool>.Ok(true, "Memory dipindah ke long memory.")
            : NotFound(ApiResponse<bool>.Fail("Short memory tidak ditemukan."));

    // The memories matching the Memory menu filters, as a file to download, with the unit test catalog of the projects
    // (includeUnitTests=false leaves it out).
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] MemoryQuery query, CancellationToken cancellationToken)
    {
        var file = await transferService.ExportAsync(query, cancellationToken);
        return File(JsonSerializer.SerializeToUtf8Bytes(file, ExportJson), "application/json", $"ait-memory-{DateTime.Now:yyyyMMdd-HHmm}.json");
    }

    // The body is an exported file. dryRun=true only reports what would happen.
    [HttpPost("import")]
    public async Task<ActionResult<ApiResponse<MemoryImportResultDto>>> Import(MemoryExportFile file, [FromQuery] MemoryImportMode mode = MemoryImportMode.Skip,
        [FromQuery] bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var result = await transferService.ImportAsync(file, mode, dryRun, cancellationToken);
        return ApiResponse<MemoryImportResultDto>.Ok(result, dryRun ? null
            : $"{result.Added} memory ditambahkan, {result.Updated} ditimpa, {result.Skipped + result.Duplicates} dilewati."
              + (result.UnitTestsAdded + result.UnitTestsUpdated > 0 ? $" Unit test: {result.UnitTestsAdded} ditambahkan, {result.UnitTestsUpdated} diperbarui." : string.Empty));
    }

    // Modules of a project (memory labels and wiki), or of knowledge memory with knowledge=true.
    [HttpGet("modules")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ProjectModuleDto>>>> Modules([FromQuery] Guid? projectId, [FromQuery] bool knowledge = false,
        CancellationToken cancellationToken = default) =>
        ApiResponse<IReadOnlyList<ProjectModuleDto>>.Ok(await catalog.GetModulesAsync(projectId, knowledge, cancellationToken));

    [HttpPut("modules/{name}")]
    public async Task<ActionResult<ApiResponse<ProjectModuleDto>>> UpdateModule(string name, SaveModuleRequest request, [FromQuery] Guid? projectId,
        [FromQuery] bool knowledge = false, CancellationToken cancellationToken = default) =>
        await catalog.UpdateModuleAsync(projectId, knowledge, name, request, cancellationToken) is { } module
            ? ApiResponse<ProjectModuleDto>.Ok(module, "Modul diperbarui.")
            : NotFound(ApiResponse<ProjectModuleDto>.Fail("Modul tidak ditemukan."));

    [HttpPost("modules/merge")]
    public async Task<ActionResult<ApiResponse<ModuleMergeDto>>> MergeModules(MergeModulesRequest request, [FromQuery] bool knowledge = false,
        CancellationToken cancellationToken = default)
    {
        var merge = await catalog.MergeAsync(request, knowledge, cancellationToken);
        return ApiResponse<ModuleMergeDto>.Ok(merge, $"Modul {merge.From} digabung ke {merge.Into}.");
    }

    [HttpPost("modules/check")]
    public async Task<ActionResult<ApiResponse<int>>> CheckModules([FromQuery] Guid? projectId, [FromQuery] bool knowledge = false,
        CancellationToken cancellationToken = default)
    {
        var found = await catalog.CheckModulesAsync(projectId, knowledge, cancellationToken);
        return ApiResponse<int>.Ok(found, found == 0 ? "Tidak ada modul ganda." : $"{found} modul ganda digabung atau disarankan.");
    }

    [HttpGet("merges")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<ModuleMergeDto>>>> Merges([FromQuery] Guid? projectId, [FromQuery] bool knowledge = false,
        CancellationToken cancellationToken = default) =>
        ApiResponse<IReadOnlyList<ModuleMergeDto>>.Ok(await catalog.GetMergesAsync(projectId, knowledge, cancellationToken));

    [HttpPost("merges/{id:guid}/undo")]
    public async Task<ActionResult<ApiResponse<bool>>> UndoMerge(Guid id, CancellationToken cancellationToken)
    {
        await catalog.UndoMergeAsync(id, cancellationToken);
        return ApiResponse<bool>.Ok(true, "Merge modul dibatalkan.");
    }

    [HttpPost("merges/{id:guid}/accept")]
    public async Task<ActionResult<ApiResponse<bool>>> AcceptMerge(Guid id, CancellationToken cancellationToken)
    {
        await catalog.AcceptMergeAsync(id, cancellationToken);
        return ApiResponse<bool>.Ok(true, "Saran merge diterapkan.");
    }

    [HttpPost("merges/{id:guid}/reject")]
    public async Task<ActionResult<ApiResponse<bool>>> RejectMerge(Guid id, CancellationToken cancellationToken)
    {
        await catalog.RejectMergeAsync(id, cancellationToken);
        return ApiResponse<bool>.Ok(true, "Saran merge ditolak.");
    }

    // Memories waiting for a label, suggested module merges and likely duplicate memories.
    [HttpGet("review")]
    public async Task<ActionResult<ApiResponse<MemoryReviewDto>>> Review([FromQuery] Guid? projectId, CancellationToken cancellationToken) =>
        ApiResponse<MemoryReviewDto>.Ok(await catalog.GetReviewAsync(projectId, cancellationToken));

    [HttpGet("relabel")]
    public async Task<ActionResult<ApiResponse<MemoryRelabelStatusDto>>> RelabelStatus(CancellationToken cancellationToken) =>
        ApiResponse<MemoryRelabelStatusDto>.Ok(await catalog.GetRelabelStatusAsync(cancellationToken));

    // The migration of older project facts into the module summaries.
    [HttpGet("changelog")]
    public async Task<ActionResult<ApiResponse<MemoryChangelogStatusDto>>> ChangelogStatus(CancellationToken cancellationToken) =>
        ApiResponse<MemoryChangelogStatusDto>.Ok(await summaryWriter.GetStatusAsync(cancellationToken));

    [HttpPost("relabel")]
    public ActionResult<ApiResponse<bool>> StartRelabel()
    {
        catalog.StartRelabel();
        return ApiResponse<bool>.Ok(true, "Migrasi label dijalankan di belakang.");
    }

    [HttpPost("consolidate")]
    public async Task<ActionResult<ApiResponse<int>>> Consolidate(CancellationToken cancellationToken)
    {
        var moved = await memoryService.ConsolidateAsync(cancellationToken);
        return ApiResponse<int>.Ok(moved, moved == 0 ? "Tidak ada short memory yang sudah lewat masa simpan." : $"{moved} memory dipindah ke long memory.");
    }
}
