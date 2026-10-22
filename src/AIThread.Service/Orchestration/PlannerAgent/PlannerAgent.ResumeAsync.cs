using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.Orchestration;

public sealed partial class PlannerAgent
{
    // Continues a conversation paused by ask_user: the developer's reply becomes the result of that tool call.
    public async Task<PlanningResult> ResumeAsync(
        PlannerInput input,
        PlannerConversation conversation,
        string toolResult,
        ThreadRecorder recorder,
        ClientProgress progress,
        CancellationToken cancellationToken)
    {
        if (input.Nodes.Count == 0) throw new OrchestrationException("Belum ada node Planner aktif. Tambahkan di menu Node.");

        // The planner that asked, when it is still active; otherwise the preferred one takes the conversation over.
        var node = input.Nodes.FirstOrDefault(n => n.Id == conversation.NodeId) ?? input.Nodes[0];
        var groups = await ToolsNodeGroupsAsync(node, conversation.CanBuild, conversation.CanGit, cancellationToken);
        var messages = conversation.Messages;
        messages[0] = await SystemMessageAsync(node, groups, input.AutoTesting, input.UnitTests?.Settings.Authoring, cancellationToken);
        messages.Add(ChatMessages.Tool(conversation.AskCallId, toolResult));

        var run = new PlannerRun
        {
            Messages = messages,
            Node = node,
            Step = conversation.Step,
            Tokens = conversation.Tokens,
            CachedTokens = conversation.CachedTokens,
            ToolsNodeTokens = conversation.ToolsNodeTokens,
            QuestionsAsked = conversation.QuestionsAsked,
            CanBuild = conversation.CanBuild,
            CanGit = conversation.CanGit,
            MaxSteps = Math.Max(input.Settings.PlannerMaxSteps, conversation.Step + StepsAfterAnswer),
            Groups = groups
        };
        return await RunAsync(input, run, new WorkspaceTools(input.Scope), recorder, progress, cancellationToken);
    }
}
