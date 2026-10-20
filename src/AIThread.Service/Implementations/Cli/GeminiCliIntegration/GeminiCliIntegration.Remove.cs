namespace AIThread.Service.Implementations.Cli;

public sealed partial class GeminiCliIntegration
{
    protected override string Remove(string content) => ManagedTextBlock.Remove(content);
}
