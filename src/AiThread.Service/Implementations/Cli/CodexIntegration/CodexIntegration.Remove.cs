namespace AiThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    protected override string Remove(string content) => ManagedTextBlock.Remove(content);
}
