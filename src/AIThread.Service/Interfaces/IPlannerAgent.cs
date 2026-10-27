using AIThread.Common.Enums;
using AIThread.Service.Orchestration;

namespace AIThread.Service.Interfaces;

public interface IPlannerAgent
{
    Task<PlanningResult> PlanAsync(PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);

    Task<PlanningResult> ResumeAsync(PlannerInput input, PlannerConversation conversation, string toolResult, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);

    // Continues a conversation paused on a permission request: decision is the developer's answer (null when the reply
    // was not one, which counts as a denial carrying that reply as a new instruction).
    Task<PlanningResult> ResumeApprovalAsync(PlannerInput input, PlannerConversation conversation, PlannerApproval approval, ApprovalDecision? decision, string reply,
        ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
