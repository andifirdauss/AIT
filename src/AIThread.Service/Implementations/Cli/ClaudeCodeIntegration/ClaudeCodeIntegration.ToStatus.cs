using AIThread.Common.DTOs;
using AIThread.Common.Enums;

namespace AIThread.Service.Implementations.Cli;

public sealed partial class ClaudeCodeIntegration
{
    private static CliStatusDto ToStatus(CliScope scope, ClaudeSettingsStatusDto s) =>
        new(Id, scope, s.Path, s.FileExists, s.PointsToAIThread, s.CurrentBaseUrl, s.PreviewJson);
}
