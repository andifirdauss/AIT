using AIThread.Service.Models;

namespace AIThread.Service.Orchestration;

public sealed partial class ThreadOrchestrator
{
    // The chat sends its whole history with every prompt, the "login <user> <password>" reply included; the passwords
    // given for testing are masked before any of it reaches a model or the database. So are the secrets in it (the
    // CLI's own reads of .env or appsettings.json travel in the conversation); a value given in the prompt still gets
    // written, since the executor's placeholder is restored on write.
    private ThreadRequest Redacted(ThreadRequest request) => request with
    {
        Prompt = secrets.Redact(testLogins.Redact(request.Prompt)!),
        PromptContext = secrets.RedactNullable(testLogins.Redact(request.PromptContext)),
        ConversationContext = secrets.RedactNullable(testLogins.Redact(request.ConversationContext))
    };
}
