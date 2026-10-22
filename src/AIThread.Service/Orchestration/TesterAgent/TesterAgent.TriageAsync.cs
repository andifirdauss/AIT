using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class TesterAgent
{
    public async Task<UnitTestTriageResult> TriageAsync(UnitTestTriageInput input, ThreadRecorder recorder, ClientProgress progress, CancellationToken cancellationToken)
    {
        try
        {
            var brief = TriageBrief(input);
            var reply = await caller.ChatAsync(input.Tester, async candidate => new ChatCompletionRequest
            {
                Model = candidate.Model,
                Messages =
                [
                    ChatMessages.System(await prompts.BuildAsync(AgentPrompts.TesterTriage, candidate, cancellationToken)),
                    ChatMessages.User(brief)
                ],
                Temperature = candidate.Temperature ?? 0.1,
                MaxTokens = candidate.MaxTokens
            }, cancellationToken, onSwitch: s => s.ReportAsync("Tester", recorder, progress, cancellationToken));
            return ParseTriage(reply.Result.Content, input.Failures) with { Tokens = reply.Result.TotalTokens, Node = reply.Node };
        }
        catch (NineRouterException ex)
        {
            return new UnitTestTriageResult([], null, 0, input.Tester, ex.Message);
        }
    }
}
