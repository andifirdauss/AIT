using AiThread.Service.Models;
using AiThread.Service.Verification;
using AiThread.Service.Workspace;

namespace AiThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    public async Task<PlanningResult> PlanAsync(PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (input.Nodes.Count == 0) throw new OrchestrationException("Belum ada node Planner aktif. Tambahkan di menu Node.");

        var tools = new WorkspaceTools(input.Scope);
        var build = input.Settings.BuildEnabled ? BuildCommandResolver.Resolve(input.Project.RootPath, input.Project.Modules, null, out _) : null;
        var (brief, relevant) = await BuildBriefAsync(input, tools, build, cancellationToken);
        await progress.StepAsync(LabelLine(input.CodeMap, relevant, input.LabelsFromSession), cancellationToken, hang: 3);
        var groups = await ToolsNodeGroupsAsync(input.Nodes[0], build is not null, cancellationToken);

        var run = new PlannerRun
        {
            Messages = [await SystemMessageAsync(input.Nodes[0], groups, cancellationToken), ChatMessages.User(brief, input.Request.Images)],
            Node = input.Nodes[0],
            CanBuild = build is not null,
            MaxSteps = input.Settings.PlannerMaxSteps,
            Groups = groups
        };
        return await RunAsync(input, run, tools, recorder, progress, cancellationToken);
    }
}
