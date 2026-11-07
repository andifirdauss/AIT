namespace AIThread.Service.Git;

public sealed partial class CommitIngestor
{
    // Whether the commit brings back commits summarized before, instead of a change of its own:
    // - the same patch-id as one (a rebase or cherry-pick);
    // - every file it keeps holds a blob a summarized commit of another branch wrote, or an AIT thread wrote, and at least
    //   one is a summarized commit's (a squash merge of a working branch). It then also stands for the earlier commits of
    //   those branches whose files it all touches (their content was overwritten by the later ones, but the squash holds
    //   them too). Commits of the same branch are left out: a commit there that puts a file back as an earlier one left it
    //   (a revert) is a change of its own.
    // Null: summarize it.
    private static IngestCopy? CopyOf(CommitInfo commit, string? patch, string? branch, Summarized summarized, IReadOnlySet<string> threadFiles)
    {
        if (patch is not null && summarized.Patches.TryGetValue(patch, out var same))
            return new IngestCopy([same.Id], $"copy of {same.Commit[..Math.Min(8, same.Commit.Length)]}");

        var kept = commit.Files.Where(f => !f.Removed).ToList();
        if (kept.Count == 0) return null;
        var owners = new HashSet<Guid>();
        var branches = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in kept)
        {
            var key = $"{file.Path}|{file.Blob}";
            if (summarized.Blobs.TryGetValue(key, out var owner) && owner.Branch is not null && !string.Equals(owner.Branch, branch, StringComparison.Ordinal))
            {
                owners.Add(owner.Id);
                branches.Add(owner.Branch);
            }
            else if (!threadFiles.Contains(key))
                return null;
        }
        if (owners.Count == 0) return null;

        var paths = commit.Files.Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var squashed = summarized.BranchCommits.Where(c => branches.Contains(c.Branch) && c.Paths.Count > 0 && c.Paths.All(paths.Contains)).Select(c => c.Id);
        var joined = owners.Concat(squashed).Distinct().ToList();
        return new IngestCopy(joined, $"squash of {joined.Count} commit(s)");
    }
}
