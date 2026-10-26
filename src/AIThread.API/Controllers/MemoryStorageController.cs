using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// "Penyimpanan memory" tab of the Setting page: which provider (and server) holds short and long memory.
[ApiController]
[Route("api/settings/memory-storage")]
public class MemoryStorageController(IMemoryStorageService memoryStorage, IActivityLogService activityLog) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<MemoryStorageSettingsDto>>> Get(CancellationToken cancellationToken) =>
        ApiResponse<MemoryStorageSettingsDto>.Ok(await memoryStorage.GetAsync(cancellationToken));

    [HttpPut]
    public async Task<ActionResult<ApiResponse<MemoryStorageUpdateResultDto>>> Update(UpdateMemoryStorageRequest request, CancellationToken cancellationToken)
    {
        var result = await memoryStorage.UpdateAsync(request, cancellationToken);
        var copied = result.CopiedShort + result.CopiedLong > 0
            ? Lang.T($" {result.CopiedShort} short & {result.CopiedLong} long memory disalin ke penyimpanan baru.", $" {result.CopiedShort} short & {result.CopiedLong} long memories copied to the new storage.")
            : string.Empty;
        activityLog.Info("setting",
            Lang.T($"Penyimpanan memory disimpan: short = {result.Settings.ShortStatus.Description}, long = {result.Settings.LongStatus.Description}.{copied}", $"Memory storage saved: short = {result.Settings.ShortStatus.Description}, long = {result.Settings.LongStatus.Description}.{copied}"));
        foreach (var warning in result.Warnings) activityLog.Warn("memory", warning);

        return ApiResponse<MemoryStorageUpdateResultDto>.Ok(result, Lang.T($"Penyimpanan memory tersimpan.{copied}", $"Memory storage saved.{copied}"));
    }

    [HttpPost("test")]
    public async Task<ActionResult<ApiResponse<MemoryStoreTestResultDto>>> Test(TestMemoryStoreRequest request, CancellationToken cancellationToken) =>
        ApiResponse<MemoryStoreTestResultDto>.Ok(await memoryStorage.TestAsync(request, cancellationToken));
}
