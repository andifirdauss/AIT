using AIThread.Service.Proxy;

namespace AIThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    // Also recognizes the header of earlier versions (X-AIThread-Solution), so applying again replaces it.
    private static bool IsAIThreadHeader(string value) =>
        value.StartsWith(ProjectHeader, StringComparison.OrdinalIgnoreCase)
        || value.StartsWith(ClaudeRequestInspector.LegacySolutionHeader, StringComparison.OrdinalIgnoreCase);
}
