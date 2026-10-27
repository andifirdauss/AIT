using AIThread.Common.Enums;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The state of one file once git's history is asked where StateOf could not tell from the index and HEAD:
    // - Committed: the commit that brought the blob; pushed when a remote-tracking branch holds it.
    // - ModifiedOutside: committed after all when HEAD's history brings the blob, or when HEAD holds the thread's lines
    //   with the developer's edits on top (Edited).
    // - Discarded: stashed, on another branch, lost (a commit that held it is no longer reachable), or really discarded
//   (a commit of HEAD's history that held it was reverted).
    private async Task<FileResult> ResolveAsync(string root, TrackedFile file, GitChangeState? state, HistoryScope scope, DateTime since,
        RefreshCache cache, Func<Task<Dictionary<string, ThreadLines>>> lines, CancellationToken cancellationToken)
    {
        switch (state)
        {
            case GitChangeState.Committed:
            {
                // HEAD holds the blob, so the newest commit of the list added it (the same content may have come and
                // gone before: a revert, then written again).
                var commit = (await CommitsOfAsync(root, file, scope, since, cache.History, cancellationToken)).FirstOrDefault();
                return await PushedAsync(root, new FileResult(GitChangeState.Committed, commit, false), cache, cancellationToken);
            }
            case GitChangeState.ModifiedOutside:
            {
                // HEAD no longer holds the blob: the newest commit dropped it, the one before added it.
                if ((await CommitsOfAsync(root, file, scope, since, cache.History, cancellationToken)) is [_, var added, ..])
                    return await PushedAsync(root, new FileResult(GitChangeState.Committed, added, false), cache, cancellationToken);
                if ((await lines()).GetValueOrDefault(file.FilePath) is { } threadLines
                    && await HeadHoldsLinesAsync(root, file.FilePath, threadLines, cache, cancellationToken)
                    && await LastCommitAsync(root, file.FilePath, since, cache.LastCommits, cancellationToken) is { } edited)
                    return await PushedAsync(root, new FileResult(GitChangeState.Committed, edited, true), cache, cancellationToken);
                return new FileResult(GitChangeState.ModifiedOutside, null, false);
            }
            case GitChangeState.Discarded:
            {
                if (file.AfterBlob is { } blob && (await StashBlobsAsync(root, cache, cancellationToken)).GetValueOrDefault(file.FilePath)?.Contains(blob) == true)
                    return new FileResult(GitChangeState.Stashed, null, false);
                // A commit of HEAD's own history means the change was reverted here: discarded, not elsewhere.
                if ((await CommitsOfAsync(root, file, scope, since, cache.History, cancellationToken)).LastOrDefault() is { } other
                    && !await InHeadAsync(root, other, cache, cancellationToken))
                    return new FileResult(GitChangeState.OnOtherBranch, other, false);
                // It was committed before (here or on a branch) and no branch or tag holds that commit now.
                if (file.Commit is { } dropped && !await ReachableAsync(root, dropped, cache, cancellationToken))
                    return new FileResult(GitChangeState.Lost, dropped, file.Edited);
                return new FileResult(GitChangeState.Discarded, null, false);
            }
            default:
                return new FileResult(state, null, false);
        }
    }
}
