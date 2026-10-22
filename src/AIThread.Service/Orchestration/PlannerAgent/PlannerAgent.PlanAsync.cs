using AIThread.Service.Models;
using AIThread.Service.Verification;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    public async Task<PlanningResult> PlanAsync(PlannerInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        if (input.Nodes.Count == 0) throw new OrchestrationException("Belum ada node Planner aktif. Tambahkan di menu Node.");

        input = await WithAccountsAsync(input, recorder, cancellationToken);
        var tools = new WorkspaceTools(input.Scope, secrets);
        var build = input.Settings.BuildEnabled ? BuildCommandResolver.Resolve(input.Project.RootPath, input.Project.Modules, null, out _) : null;
        var (brief, relevant) = await BuildBriefAsync(input, tools, build, cancellationToken);
        await progress.StepAsync(LabelLine(input.CodeMap, relevant, input.LabelsFromSession), cancellationToken, hang: 3);
        var canGit = GitTools.IsRepository(input.Scope.Root);
        var groups = await ToolsNodeGroupsAsync(input.Nodes[0], build is not null, canGit, cancellationToken);

        var run = new PlannerRun
        {
            Messages = [await SystemMessageAsync(input.Nodes[0], groups, input.AutoTesting, input.UnitTests?.Settings.Authoring, cancellationToken), ChatMessages.User(brief, input.Request.Images)],
            Node = input.Nodes[0],
            CanBuild = build is not null,
            CanGit = canGit,
            MaxSteps = input.Settings.PlannerMaxSteps,
            Groups = groups
        };
        return await RunAsync(input, run, tools, recorder, progress, cancellationToken);
    }
}
