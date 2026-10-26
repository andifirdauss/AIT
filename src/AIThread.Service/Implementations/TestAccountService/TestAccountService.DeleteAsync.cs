using AIThread.Common.Localization;
using AIThread.Service.Interfaces;
namespace AIThread.Service.Implementations;

public sealed partial class TestAccountService
{
    public async Task DeleteAsync(Guid projectId, Guid accountId, CancellationToken cancellationToken = default)
    {
        var (project, _) = await ContextAsync(projectId, cancellationToken);
        await store.DeleteAsync(project.Key, accountId, cancellationToken);
        activityLog.Info(LogCategory, Lang.T($"Satu akun uji dihapus dari {project.Name}.", $"One test account deleted from {project.Name}."), projectId: projectId);
    }
}
