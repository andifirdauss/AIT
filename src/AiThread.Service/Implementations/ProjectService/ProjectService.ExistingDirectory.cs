namespace AiThread.Service.Implementations;

public sealed partial class ProjectService
{
    private static string? ExistingDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var cleaned = path.Trim().Trim('`', '"', '\'');
            var wasIndex = cleaned.IndexOf("(was", StringComparison.OrdinalIgnoreCase);
            if (wasIndex >= 0) cleaned = cleaned[..wasIndex].Trim().Trim('`', '"', '\'');
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cleaned));
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
