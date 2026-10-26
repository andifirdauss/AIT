using AIThread.Common.DTOs;
using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Testing;

namespace AIThread.Service.Implementations;

public sealed partial class TestAccountService
{
    public async Task<TestAccountsDto> GetAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var project = await projectService.GetContextAsync(projectId, cancellationToken) ?? throw new AppValidationException(Lang.T("Project tidak ditemukan.", "Project not found."));
        if (string.IsNullOrEmpty(project.Key))
            return new TestAccountsDto(null, false, NoKey, [], TestAccountText.ToDto(new TestingSettings()), []);

        var set = await store.GetAsync(project.Key, cancellationToken);
        return new TestAccountsDto(project.Key, true, null, set.Accounts.Select(TestAccountText.ToDto).ToList(), TestAccountText.ToDto(set.Settings),
            TestAccountText.AttributeKeys(set.Accounts));
    }
}
