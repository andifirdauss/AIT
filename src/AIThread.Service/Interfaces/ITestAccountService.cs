using AIThread.Common.DTOs;

namespace AIThread.Service.Interfaces;

// Project > Detail → Akun uji: the test accounts of one project, its development password and its "login as" detection.
// Every developer of the project may change them.
public interface ITestAccountService
{
    // Works without a project key too: CanSave is then false with the reason.
    Task<TestAccountsDto> GetAsync(Guid projectId, CancellationToken cancellationToken = default);

    Task<TestAccountDto> AddAsync(Guid projectId, SaveTestAccountRequest request, CancellationToken cancellationToken = default);

    Task<TestAccountDto> UpdateAsync(Guid projectId, Guid accountId, SaveTestAccountRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid projectId, Guid accountId, CancellationToken cancellationToken = default);

    Task<TestingSettingsDto> SaveSettingsAsync(Guid projectId, SaveTestingSettingsRequest request, CancellationToken cancellationToken = default);

    // The developer's correction or confirmation of the "login as" detection.
    Task<TestingSettingsDto> SaveImpersonationAsync(Guid projectId, SaveImpersonationRequest request, CancellationToken cancellationToken = default);

    // Looks for "login as" in the project's code again (a scan, then a cheap model) and keeps the result unconfirmed.
    Task<TestingSettingsDto> DetectImpersonationAsync(Guid projectId, CancellationToken cancellationToken = default);
}
