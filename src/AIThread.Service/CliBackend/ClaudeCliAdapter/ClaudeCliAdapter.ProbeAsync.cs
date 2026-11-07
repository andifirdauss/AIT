using AIThread.Common.DTOs;

namespace AIThread.Service.CliBackend;

public sealed partial class ClaudeCliAdapter
{
    private Task<CliBackendStatusDto> ProbeAsync(CancellationToken cancellationToken) =>
        CliProcess.ProbeAsync(CliModelId.Claude, DisplayName, Command, ResolveLaunch(), CleanEnvironment, logger, cancellationToken);
}
