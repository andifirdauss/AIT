using AIThread.Service.Interfaces;
using AIThread.Common.DTOs;
using AIThread.Service.Testing;

namespace AIThread.Service.Implementations;

public sealed partial class TestAccountService
{
    public async Task<TestAccountDto> UpdateAsync(Guid projectId, Guid accountId, SaveTestAccountRequest request, CancellationToken cancellationToken = default)
    {
        var (project, by) = await ContextAsync(projectId, cancellationToken);
        var account = await store.UpdateAsync(project.Key, accountId, request, by, cancellationToken);
        activityLog.Info(LogCategory, $"Akun uji {account.Username} di {project.Name} diubah.", projectId: projectId);
        return TestAccountText.ToDto(account);
    }
}
