using AIThread.Service.Databases;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerToolRunner
{
    private static Task<PlannerToolOutcome> ListDatabasesAsync(PlannerInput input, string prefix, ThreadRecorder recorder, ClientProgress progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(new PlannerToolOutcome(DatabaseToolText.Databases(input.Databases ?? []), null));
}
