namespace AiThread.Service.Implementations;

public sealed partial class ClaudeSettingsService
{
    private static bool SameUrl(string a, string b) =>
        string.Equals(a.Trim().TrimEnd('/'), b.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}
