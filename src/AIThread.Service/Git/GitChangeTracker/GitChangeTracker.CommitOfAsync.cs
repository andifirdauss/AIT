using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The commit that brought the file's change, in HEAD's history or in any branch or tag: the oldest one since the
    // thread started that adds or drops the thread's blob (for a file the thread removed, the newest in HEAD that
    // deleted it). Null when none does. Answers are cached per refresh: several threads may share a file.
    private static async Task<string?> CommitOfAsync(string root, TrackedFile file, HistoryScope scope, DateTime since,
        Dictionary<string, string?> cache, CancellationToken cancellationToken)
    {
        if (scope == HistoryScope.None) return null;
        var key = $"{scope}|{file.Removed}|{file.AfterBlob}|{file.FilePath}|{since:O}";
        if (cache.TryGetValue(key, out var cached)) return cached;

        // A day of slack: the committer's clock may run behind this machine's.
        var after = "--since=" + since.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        string[] revisions = scope == HistoryScope.Head ? ["HEAD"] : ["--branches", "--remotes", "--tags"];
        string? commit = null;
        if (file.Removed)
        {
            var result = await GitProcess.RunAsync(root, ["--literal-pathspecs", "log", "-n1", "--format=%H", "--diff-filter=D", after, .. revisions, "--", file.FilePath],
                cancellationToken);
            commit = result.Ok ? GitProcess.Lines(result.Output).FirstOrDefault() : null;
        }
        else if (file.AfterBlob is not null)
        {
            var result = await GitProcess.RunAsync(root, ["--literal-pathspecs", "log", "--format=%H", "--find-object=" + file.AfterBlob, after, .. revisions, "--", file.FilePath],
                cancellationToken);
            commit = result.Ok ? GitProcess.Lines(result.Output).LastOrDefault() : null;
        }
        cache[key] = commit;
        return commit;
    }
}
