using System.Text.Json;
using System.Text.Json.Serialization;
using AiThread.Common.DTOs;
using AiThread.Common.Enums;
using AiThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AiThread.API.Controllers;

[ApiController]
[Route("api/memory")]
public class MemoriesController(IMemoryService memoryService, IMemoryTransferService transferService) : ControllerBase
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

    // The memories matching the Memory menu filters, as a file to download.
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
            : $"{result.Added} memory ditambahkan, {result.Updated} ditimpa, {result.Skipped + result.Duplicates} dilewati.");
    }

    [HttpPost("consolidate")]
    public async Task<ActionResult<ApiResponse<int>>> Consolidate(CancellationToken cancellationToken)
    {
        var moved = await memoryService.ConsolidateAsync(cancellationToken);
        return ApiResponse<int>.Ok(moved, moved == 0 ? "Tidak ada short memory yang sudah lewat masa simpan." : $"{moved} memory dipindah ke long memory.");
    }
}
