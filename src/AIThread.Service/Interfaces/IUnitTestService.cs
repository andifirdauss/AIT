using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

// The unit test catalog of one project as Project > Detail → Unit test works with it. The source snapshots never leave
// the service layer.
public interface IUnitTestService
{
    Task<UnitTestCatalogDto> GetAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<UnitTestSettingsDto> SaveSettingsAsync(Guid projectId, SaveUnitTestSettingsRequest request, CancellationToken cancellationToken = default);

    // Finds the tests in the code, adds the new ones and labels them with a cheap model.
    Task<UnitTestScanResultDto> ScanAsync(Guid projectId, CancellationToken cancellationToken = default);

    // Runs the chosen tests now (an environment skip remembered on this machine is tried again) and records the results.
    Task<UnitTestRunResultDto> RunAsync(Guid projectId, RunUnitTestsRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<UnitTestHistoryDto>> HistoryAsync(Guid projectId, Guid testId, CancellationToken cancellationToken = default);

    // An obsolete or quarantined test runs again.
    Task<UnitTestCaseDto> ReactivateAsync(Guid projectId, Guid testId, CancellationToken cancellationToken = default);

    // The developer confirms an obsolete test is gone: Removed in the catalog (never restored again) and, with
    // RemoveCode, a thread deletes its code (recorded, and undoable like any thread).
    Task<ConfirmUnitTestRemovalResultDto> ConfirmRemovalAsync(Guid projectId, Guid testId, ConfirmUnitTestRemovalRequest request, CancellationToken cancellationToken = default);
}
