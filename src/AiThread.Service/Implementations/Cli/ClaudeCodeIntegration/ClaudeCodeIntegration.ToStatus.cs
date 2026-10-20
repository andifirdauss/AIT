using AiThread.Common.DTOs;
using AiThread.Common.Enums;

namespace AiThread.Service.Implementations.Cli;

public sealed partial class ClaudeCodeIntegration
{
    private static CliStatusDto ToStatus(CliScope scope, ClaudeSettingsStatusDto s) =>
        new(Id, scope, s.Path, s.FileExists, s.PointsToAiThread, s.CurrentBaseUrl, s.PreviewJson);
}
