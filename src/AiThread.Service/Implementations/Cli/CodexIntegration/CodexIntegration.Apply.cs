using AiThread.Service.Models;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    protected override string Apply(string content, CliEntry entry)
    {
        if (Conflict(content) is { } conflict) throw new AppValidationException(conflict);
        return ManagedTextBlock.Append(content, Block(entry));
    }
}
