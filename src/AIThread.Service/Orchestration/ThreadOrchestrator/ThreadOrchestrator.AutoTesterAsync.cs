using AIThread.Common.Entities;
using AIThread.Common.Enums;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The tester of a run: the planner has Auto Testing on and a Tester node is active (the one with the smallest
    // priority). Without it the plan gets no test section and nothing is tested.
    private async Task<AiNode?> AutoTesterAsync(AiNode? planner, ThreadRecorder recorder, CancellationToken cancellationToken)
    {
        if (planner is not { AutoTesting: true }) return null;
        var tester = (await nodeService.GetActiveAsync(NodeRole.Tester, cancellationToken)).FirstOrDefault();
        if (tester is null) recorder.Log(ActivityLevel.Warning, "Auto Testing menyala di planner, tetapi belum ada node Tester aktif; testing dilewati.");
        return tester;
    }
}
