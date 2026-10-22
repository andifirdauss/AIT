using AIThread.Common.DTOs;
using AIThread.Common.Entities;

namespace AIThread.Service.Interfaces;

// The unit test catalog of the projects, in the store that holds long memory (shared by the team when that store is
// external). Rows are keyed by ProjectKey.
public interface IUnitTestCatalog
{
    // The project's settings, or the defaults when none were saved.
    Task<UnitTestSettings> GetSettingsAsync(string projectKey, CancellationToken cancellationToken);

    Task<UnitTestSettings> SaveSettingsAsync(string projectKey, SaveUnitTestSettingsRequest request, string by, CancellationToken cancellationToken);

    // Every test of the project, removed ones included, ordered by number. Untracked copies.
    Task<IReadOnlyList<UnitTestCase>> ListAsync(string projectKey, CancellationToken cancellationToken);

    Task<UnitTestCase?> GetAsync(Guid id, CancellationToken cancellationToken);

    // Adds tests with the next free numbers of the project (Number is assigned here) and returns them.
    Task<IReadOnlyList<UnitTestCase>> AddAsync(string projectKey, IReadOnlyList<UnitTestCase> tests, CancellationToken cancellationToken);

    // Saves the given rows as they are now.
    Task UpdateAsync(IReadOnlyList<UnitTestCase> tests, CancellationToken cancellationToken);

    // Adds run history and keeps only the latest runs per test.
    Task RecordRunsAsync(IReadOnlyList<UnitTestRun> runs, CancellationToken cancellationToken);

    Task<IReadOnlyList<UnitTestRun>> HistoryAsync(Guid testId, int take, CancellationToken cancellationToken);

    // The latest run of each test of the project on one machine (environment skips are remembered per machine).
    Task<IReadOnlyDictionary<Guid, UnitTestRun>> LastRunsOnMachineAsync(string projectKey, string machine, CancellationToken cancellationToken);

    // Undoes what a rolled back thread did to the catalog: its new tests go, the tests it made obsolete are active again.
    Task<int> RevertThreadAsync(Guid threadId, CancellationToken cancellationToken);
}
