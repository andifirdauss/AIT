using AIThread.Service.Interfaces;

namespace AIThread.Service.CliBackend;

public sealed partial class CliModelService(IClaudeCliAdapter claude) : ICliModelService
{
    public const string ClaudeOwner = "Claude Code (CLI)";

    // Claude Code's model aliases: each follows the provider's newest model of that family.
    private static readonly string[] ClaudeAliases = ["opus", "sonnet", "haiku", "fable"];
}
