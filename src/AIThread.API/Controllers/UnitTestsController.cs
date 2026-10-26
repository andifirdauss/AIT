using AIThread.Common.DTOs;
using AIThread.Common.Localization;
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
        ApiResponse<UnitTestSettingsDto>.Ok(await unitTests.SaveSettingsAsync(projectId, request, cancellationToken), Lang.T("Setting unit test disimpan.", "Unit test settings saved."));

    // Finds the tests in the code and labels the new ones with a cheap model.
    [HttpPost("scan")]
    public async Task<ActionResult<ApiResponse<UnitTestScanResultDto>>> Scan(Guid projectId, CancellationToken cancellationToken)
    {
        var result = await unitTests.ScanAsync(projectId, cancellationToken);
        return ApiResponse<UnitTestScanResultDto>.Ok(result, Lang.T($"{result.Found} test ditemukan: {result.Added} baru, {result.Moved} pindah file, {result.Labeled} diberi label.", $"{result.Found} tests found: {result.Added} new, {result.Moved} moved, {result.Labeled} labeled."));
    }

    // Runs the chosen tests now (all=true: every active test).
    [HttpPost("run")]
    public async Task<ActionResult<ApiResponse<UnitTestRunResultDto>>> Run(Guid projectId, RunUnitTestsRequest request, CancellationToken cancellationToken)
    {
        var result = await unitTests.RunAsync(projectId, request, cancellationToken);
        return ApiResponse<UnitTestRunResultDto>.Ok(result, Lang.T($"{result.Ran} test: {result.Passed} lolos, {result.Failed} gagal, {result.Skipped} dilewati.", $"{result.Ran} tests: {result.Passed} passed, {result.Failed} failed, {result.Skipped} skipped."));
    }

    [HttpGet("{testId:guid}/history")]
    public async Task<ActionResult<ApiResponse<IReadOnlyList<UnitTestHistoryDto>>>> History(Guid projectId, Guid testId, CancellationToken cancellationToken) =>
        ApiResponse<IReadOnlyList<UnitTestHistoryDto>>.Ok(await unitTests.HistoryAsync(projectId, testId, cancellationToken));

    // An obsolete or quarantined test is active again.
    [HttpPost("{testId:guid}/reactivate")]
    public async Task<ActionResult<ApiResponse<UnitTestCaseDto>>> Reactivate(Guid projectId, Guid testId, CancellationToken cancellationToken)
    {
        var test = await unitTests.ReactivateAsync(projectId, testId, cancellationToken);
        return ApiResponse<UnitTestCaseDto>.Ok(test, Lang.T($"{test.DisplayNumber} aktif lagi.", $"{test.DisplayNumber} active again."));
    }

    // The developer confirms an obsolete test is gone; removeCode starts a thread that deletes its code.
    [HttpPost("{testId:guid}/confirm-removal")]
    public async Task<ActionResult<ApiResponse<ConfirmUnitTestRemovalResultDto>>> ConfirmRemoval(Guid projectId, Guid testId, ConfirmUnitTestRemovalRequest request,
        CancellationToken cancellationToken)
    {
        var result = await unitTests.ConfirmRemovalAsync(projectId, testId, request, cancellationToken);
        return ApiResponse<ConfirmUnitTestRemovalResultDto>.Ok(result, result.ThreadId is null
            ? Lang.T($"{result.Test.DisplayNumber} dihapus dari katalog.", $"{result.Test.DisplayNumber} deleted from the catalog.")
            : Lang.T($"{result.Test.DisplayNumber} dihapus dari katalog; thread penghapus kodenya sedang berjalan.", $"{result.Test.DisplayNumber} deleted from the catalog; a thread deleting its code is running."));
    }
}
