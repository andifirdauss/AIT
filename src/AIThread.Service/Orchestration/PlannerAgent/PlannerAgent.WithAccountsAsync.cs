using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // With Auto Testing, the project's test accounts: the brief lists them and a scenario's actor must be one of them.
    // A store that cannot be read only costs the list.
    private async Task<PlannerInput> WithAccountsAsync(PlannerInput input, ThreadRecorder recorder, CancellationToken cancellationToken)
    {
        if (!input.AutoTesting || input.TestAccounts is not null || string.IsNullOrEmpty(input.Project.Key)) return input;
        try
        {
            return input with { TestAccounts = await accountStore.GetAsync(input.Project.Key, cancellationToken) };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            recorder.Log(ActivityLevel.Warning, $"Akun uji project tidak bisa dibaca, planner tanpa daftar akun: {ex.Message}");
            return input;
        }
    }
}
