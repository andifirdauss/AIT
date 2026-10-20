using AIThread.Service.Orchestration;
using AIThread.Service.Workspace;

namespace AIThread.Service.Interfaces;

public interface IToolsNodeAgent
{
    Task<ToolsNodeInvestigation> InvestigateAsync(ToolsNodeGroup group, string question, PlannerInput input, WorkspaceTools tools, HashSet<Guid> memoriesRead, string label, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
