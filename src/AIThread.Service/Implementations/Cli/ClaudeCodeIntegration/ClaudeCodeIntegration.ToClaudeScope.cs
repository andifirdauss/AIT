using AIThread.Common.Enums;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class ClaudeCodeIntegration
{
    private static ClaudeSettingsScope ToClaudeScope(CliScope scope) =>
        scope == CliScope.Global ? ClaudeSettingsScope.User : ClaudeSettingsScope.Project;
}
