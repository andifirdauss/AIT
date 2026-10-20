using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface IPlannerAgent
{
    Task<PlanningResult> PlanAsync(PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);

    Task<PlanningResult> ResumeAsync(PlannerInput input, PlannerConversation conversation, string toolResult, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
