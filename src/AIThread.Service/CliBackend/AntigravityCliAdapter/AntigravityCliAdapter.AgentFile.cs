namespace AIThread.Service.CliBackend;

public sealed partial class AntigravityCliAdapter
{
    // Said first in every agent prompt: with an empty tool list a model still sometimes emits a native function call,
    // which agy rejects as malformed and ends the turn.
    private const string NoFunctionCalls =
        "You cannot call functions or tools natively: never emit a function call. When the instructions below describe tools, use them only by writing the text format they describe in your reply.";

    // .agents/agents/ait/agent.md of the working folder: a workspace agent whose explicit tool list is empty, which
    // never runs commands, and whose Markdown body (the agent's system prompt, in place of Antigravity's own) is the AIT
    // agent's system prompt.
    public static string AgentFile(string systemPrompt) =>
        $"""
         ---
         name: {AgentName}
         description: One model turn for AIT. Answers in text only, without tools.
         tools: []
         mainAgent: true
         subagent: false
         commandExecutionPolicy: off
         ---
         {NoFunctionCalls}

         {(string.IsNullOrWhiteSpace(systemPrompt) ? "You are a helpful assistant." : systemPrompt.Trim())}

         """;
}
