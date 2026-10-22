using AIThread.Common.DTOs;
using AIThread.Service.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace AIThread.API.Controllers;

// The unit test catalog of a project (Project > Detail → Unit test). Source snapshots are never returned.
[ApiController]
[Route("api/projects/{projectId:guid}/unit-tests")]
public class UnitTestsController(IUnitTestService unitTests) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<UnitTestCatalogDto>>> Get(Guid projectId, CancellationToken cancellationToken) =>
        ApiResponse<UnitTestCatalogDto>.Ok(await unitTests.GetAsync(projectId, cancellationToken));

    [HttpPut("settings")]
    public async Task<ActionResult<ApiResponse<UnitTestSettingsDto>>> SaveSettings(Guid projectId, SaveUnitTestSettingsRequest request, CancellationToken cancellationToken) =>
        ApiResponse<UnitTestSettingsDto>.Ok(await unitTests.SaveSettingsAsync(projectId, request, cancellationToken), "Setting unit test disimpan.");

    // Finds the tests in the code and labels the new ones with a cheap model.
    [HttpPost("scan")]
    public async Task<ActionResult<ApiResponse<UnitTestScanResultDto>>> Scan(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await unitTests.ScanAsync(projectId, cancellationToken);
        return ApiResponse<UnitTestScanResultDto>.Ok(result, $"{result.Found} test ditemukan: {result.Added} baru, {result.Moved} pindah file, {result.Labeled} diberi label.");
    }

    // Runs the chosen tests now (all=true: every active test).
    [HttpPost("run")]
    public async Task<ActionResult<ApiResponse<UnitTestRunResultDto>>> Run(Guid projectId, RunUnitTestsRequest request, CancellationToken cancellationToken)
    {
        var result = await unitTests.RunAsync(projectId, request, cancellationToken);
        return ApiResponse<UnitTestRunResultDto>.Ok(result, $"{result.Ran} test: {result.Passed} lolos, {result.Failed} gagal, {result.Skipped} dilewati.");
    }

    [HttpGet("{testId:guid}/history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UnitTestHistoryDto>>>> History(Guid projectId, Guid testId, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<UnitTestHistoryDto>>.Ok(await unitTests.HistoryAsync(projectId, testId, cancellationToken));

    // An obsolete or quarantined test is active again.
    [HttpPost("{testId:guid}/reactivate")]
    public async Task<ActionResult<ApiResponse<UnitTestCaseDto>>> Reactivate(Guid projectId, Guid testId, CancellationToken cancellationToken)
    {
        var test = await unitTests.ReactivateAsync(projectId, testId, cancellationToken);
        return ApiResponse<UnitTestCaseDto>.Ok(test, $"{test.DisplayNumber} aktif lagi.");
    }

    // The developer confirms an obsolete test is gone; removeCode starts a thread that deletes its code.
    [HttpPost("{testId:guid}/confirm-removal")]
    public async Task<ActionResult<ApiResponse<ConfirmUnitTestRemovalResultDto>>> ConfirmRemoval(Guid projectId, Guid testId, ConfirmUnitTestRemovalRequest request,
        CancellationToken cancellationToken)
    {
        var result = await unitTests.ConfirmRemovalAsync(projectId, testId, request, cancellationToken);
        return ApiResponse<ConfirmUnitTestRemovalResultDto>.Ok(result, result.ThreadId is null
            ? $"{result.Test.DisplayNumber} dihapus dari katalog."
            : $"{result.Test.DisplayNumber} dihapus dari katalog; thread penghapus kodenya sedang berjalan.");
    }
}
