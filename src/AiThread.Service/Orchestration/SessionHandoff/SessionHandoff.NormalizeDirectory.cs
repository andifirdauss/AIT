namespace AiThread.Service.Orchestration;

public sealed partial class SessionHandoff
{
    internal static string NormalizeDirectory(string workingDirectory)
    {
        var full = workingDirectory.Trim();
        try
        {
            full = Path.GetFullPath(full);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Keep the raw value; it still matches the same session's next hook call.
        }
        full = full.TrimEnd('\\', '/');
        return Truncate(OperatingSystem.IsWindows() ? full.ToLowerInvariant() : full, 800);
    }
}
