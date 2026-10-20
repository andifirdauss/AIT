namespace AIThread.Service.Implementations;

public sealed partial class ProjectService
{
    private static bool IsSameOrInside(string path, string root)
    {
        var trimmed = Path.TrimEndingDirectorySeparator(root);
        return path.Equals(trimmed, StringComparison.OrdinalIgnoreCase)
               || path.StartsWith(trimmed + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
