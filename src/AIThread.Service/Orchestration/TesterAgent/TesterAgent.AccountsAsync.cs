using AIThread.Service.Models;
using AIThread.Service.Testing;
using Microsoft.Extensions.Logging;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    // The project's test accounts for this run. A store that cannot be read only costs switch_user: the test still runs
    // with the developer's login, if any.
    private async Task<TestAccountSet?> AccountsAsync(ProjectContext project, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(project.Key)) return null;
        try
        {
            return await accountStore.GetAsync(project.Key, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Test accounts of {Project} could not be read; the tester runs without them.", project.Name);
            return null;
        }
    }

    private static bool CanSwitchUser(TestRun run) => run.Accounts is { Accounts.Count: > 0 };
}
