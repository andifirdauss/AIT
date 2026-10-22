using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Service.Testing;

namespace AIThread.Service.Interfaces;

// The test accounts and testing settings of a project by its key, next to long memory (shared by the team like the unit
// test catalog). by: the developer name recorded on the change.
public interface ITestAccountStore
{
    // The accounts in username order and the settings (a new, unsaved row when the project has none yet).
    Task<TestAccountSet> GetAsync(string projectKey, CancellationToken cancellationToken = default);

    Task<TestAccount> AddAsync(string projectKey, SaveTestAccountRequest request, string by, CancellationToken cancellationToken = default);

    Task<TestAccount> UpdateAsync(string projectKey, Guid accountId, SaveTestAccountRequest request, string by, CancellationToken cancellationToken = default);

    Task DeleteAsync(string projectKey, Guid accountId, CancellationToken cancellationToken = default);

    // password null or empty: back to the default. source: TestingSettings.Source*.
    Task<TestingSettings> SaveDevPasswordAsync(string projectKey, string? password, string source, string by, CancellationToken cancellationToken = default);

    // null clears the detection, so it runs again.
    Task<TestingSettings> SaveImpersonationAsync(string projectKey, ImpersonationInfo? info, string by, CancellationToken cancellationToken = default);

    Task<TestingSettings> AcknowledgeSideEffectsAsync(string projectKey, string by, CancellationToken cancellationToken = default);
}
