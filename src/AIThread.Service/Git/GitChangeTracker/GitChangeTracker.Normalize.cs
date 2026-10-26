namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // Lines compared without their indentation and trailing spaces; blank and one-character lines ("}", ")") say
    // nothing about whose change it is and are left out.
    private static IReadOnlyList<string> Normalize(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 1).ToList();
}
