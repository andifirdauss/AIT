namespace AIThread.Service.Implementations.Cli;

public sealed partial class CodexIntegration
{
    protected override string Preview(CliEntry entry) =>
        string.Join("\n", Block(entry).Prepend(ManagedTextBlock.Begin).Append(ManagedTextBlock.End));
}
