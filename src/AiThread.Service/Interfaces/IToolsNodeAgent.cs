using AiThread.Service.Orchestration;
using AiThread.Service.Workspace;

namespace AiThread.Service.Interfaces;

public interface IToolsNodeAgent
{
    Task<ToolsNodeInvestigation> InvestigateAsync(ToolsNodeGroup group, string question, PlannerInput input, WorkspaceTools tools, HashSet<Guid> memoriesRead, string label, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken);
}
