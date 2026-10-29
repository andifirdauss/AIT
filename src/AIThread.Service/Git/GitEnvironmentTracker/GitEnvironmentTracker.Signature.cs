namespace AIThread.Service.Git;

public sealed partial class GitEnvironmentTracker
{
    // Stands for the changes looked for: it differs as soon as a change is added, gets a commit, or writes another blob.
    private static string Signature(IReadOnlyList<Candidate> changes)
    {
        var hash = new HashCode();
        foreach (var change in changes)
        {
            hash.Add(change.Id);
            foreach (var commit in change.Commits) hash.Add(commit, StringComparer.OrdinalIgnoreCase);
            foreach (var file in change.Files)
            {
                hash.Add(file.Path, StringComparer.OrdinalIgnoreCase);
                hash.Add(file.Blob);
                hash.Add(file.Removed);
            }
        }
        return $"{changes.Count}:{hash.ToHashCode()}";
    }
}
