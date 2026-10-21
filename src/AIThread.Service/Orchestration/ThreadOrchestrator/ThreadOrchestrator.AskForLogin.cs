namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The tester stopped for a login it was allowed to ask for: the session's next "login <user> <password>" answers it.
    private TestingResult AskForLogin(TestingResult result, string? sessionId, Guid projectId, Guid threadId)
    {
        if (result.Report.LoginNeeded is null || string.IsNullOrEmpty(sessionId)) return result;
        testLogins.Ask(sessionId, projectId, threadId);
        result.Report.AskedLogin = true;
        return result with { ForModels = TesterAgent.ForModels(result.Report) };
    }
}
