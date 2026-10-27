using AIThread.Common.Enums;

namespace AIThread.Service.Git;

// What the planner reads next to a memory whose change is not in the branch checked out here (for the model: English).
public static class GitNotes
{
    public static string? ForPlanner(MemoryGitPresence presence)
    {
        var by = Name(presence.Author) is { } name ? $" by {name}" : string.Empty;
        var on = presence.Branch is { Length: > 0 } branch && branch != "HEAD" ? $" on {branch}" : string.Empty;
        return presence.Place switch
        {
            MemoryGitPlace.OtherBranch => $"not in this branch: committed{on}{by}",
            MemoryGitPlace.InProgress when presence.State == GitChangeState.Stashed => $"not in this branch: stashed{on}{by}",
            MemoryGitPlace.InProgress => $"not in this branch: not committed yet{on}{by}",
            _ => null
        };
    }

    // "Andi <andi@example.com>" → "Andi"; an author without a name keeps the address.
    private static string? Name(string? author)
    {
        if (string.IsNullOrWhiteSpace(author)) return null;
        var open = author.IndexOf('<');
        return open > 0 ? author[..open].Trim() : author.Trim('<', '>', ' ');
    }
}
