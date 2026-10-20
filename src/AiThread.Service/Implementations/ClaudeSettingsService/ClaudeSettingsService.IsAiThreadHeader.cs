using AiThread.Service.Proxy;

namespace AiThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    // Also recognizes the header of earlier versions (X-AiThread-Solution), so applying again replaces it.
    private static bool IsAiThreadHeader(string value) =>
        value.StartsWith(ProjectHeader, StringComparison.OrdinalIgnoreCase)
        || value.StartsWith(ClaudeRequestInspector.LegacySolutionHeader, StringComparison.OrdinalIgnoreCase);
}
