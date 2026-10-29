using Microsoft.EntityFrameworkCore;

namespace AIThread.Service.Business;

public sealed partial class BusinessEnvService
{
    // Whether the environment is registered for the project key (its pages are kept); otherwise it is a remote branch,
    // whose pages are worked out on request.
    private async Task<bool> IsRegisteredAsync(string projectKey, string environment, CancellationToken cancellationToken)
    {
        await using var git = await longStore.CreateGitAsync(cancellationToken);
        return await git.ProjectEnvironments.AsNoTracking().AnyAsync(e => e.ProjectKey == projectKey && e.Name == environment, cancellationToken);
    }
}
