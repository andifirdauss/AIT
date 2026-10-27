using System.Text.RegularExpressions;
using AIThread.Common.Enums;

namespace AIThread.Service.Approvals;

// How a target is written down and whether a saved grant covers an action: a folder grant covers everything below
// it, the other kinds only the same target.
public static partial class ApprovalTargets
{
    public static string Normalize(ApprovalKind kind, string target)
    {
        var text = target.Trim();
        return kind switch
        {
            ApprovalKind.WriteOutside or ApprovalKind.Commit => Folder(text),
            ApprovalKind.Push => text.Split('|', 2) is [var repo, var branch] ? $"{Folder(repo)}|{branch.Trim()}" : text,
            ApprovalKind.Command => SpacesRegex().Replace(text, " "),
            ApprovalKind.Host => text.ToLowerInvariant(),
            _ => text
        };
    }

    public static bool Covers(ApprovalKind kind, string granted, string target)
    {
        granted = Normalize(kind, granted);
        target = Normalize(kind, target);
        return kind switch
        {
            ApprovalKind.WriteOutside => string.Equals(granted, target, StringComparison.OrdinalIgnoreCase)
                                         || target.StartsWith(granted.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase),
            ApprovalKind.Command => string.Equals(granted, target, StringComparison.Ordinal),
            _ => string.Equals(granted, target, StringComparison.OrdinalIgnoreCase)
        };
    }

    // Full path with forward slashes and no trailing slash, as the developer sees it in the list.
    private static string Folder(string path)
    {
        try
        {
            path = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
        var trimmed = Path.TrimEndingDirectorySeparator(path).Replace('\\', '/');
        return trimmed.Length == 0 ? path.Replace('\\', '/') : trimmed;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacesRegex();
}
