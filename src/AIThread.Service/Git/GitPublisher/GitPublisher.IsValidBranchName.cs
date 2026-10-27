namespace AIThread.Service.Git;

public sealed partial class GitPublisher
{
    private static readonly char[] ForbiddenBranchChars = ['~', '^', ':', '?', '*', '[', '\\'];

    // The basic rules of git check-ref-format --branch: no spaces or control characters, no "..", "@{", "//" or
    // ~ ^ : ? * [ \, no part starting with "." or ending with ".lock", not starting with "-" or "/", not ending with
    // "/" or ".", and not "@".
    public static bool IsValidBranchName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name == "@" || name.Length > 250) return false;
        if (name.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)) || name.IndexOfAny(ForbiddenBranchChars) >= 0) return false;
        if (name.Contains("..", StringComparison.Ordinal) || name.Contains("@{", StringComparison.Ordinal) || name.Contains("//", StringComparison.Ordinal)) return false;
        if (name[0] is '-' or '/' || name[^1] is '/' or '.') return false;
        return name.Split('/').All(part => !part.StartsWith('.') && !part.EndsWith(".lock", StringComparison.OrdinalIgnoreCase));
    }
}
