using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The chat sends its whole history with every prompt, the "login <user> <password>" reply included; the passwords
    // given for testing are masked before any of it reaches a model or the database.
    private ThreadRequest Redacted(ThreadRequest request) => request with
    {
        Prompt = testLogins.Redact(request.Prompt)!,
        PromptContext = testLogins.Redact(request.PromptContext),
        ConversationContext = testLogins.Redact(request.ConversationContext)
    };
}
