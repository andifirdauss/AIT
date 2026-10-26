using AIThread.Common.DTOs;
using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
using AIThread.Service.Testing;

namespace AIThread.Service.Implementations;

public sealed partial class TestAccountService
{
    public async Task<TestAccountDto> AddAsync(Guid projectId, SaveTestAccountRequest request, CancellationToken cancellationToken = default)
    {
        var (project, by) = await ContextAsync(projectId, cancellationToken);
        var account = await store.AddAsync(project.Key, request, by, cancellationToken);
        activityLog.Info(LogCategory, Lang.T($"Akun uji {account.Username} ditambahkan ke {project.Name}.", $"Test account {account.Username} added to {project.Name}."), projectId: projectId);
        return TestAccountText.ToDto(account);
    }
}
