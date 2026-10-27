using AIThread.Service.Workspace;

namespace AIThread.Service.Git;

public sealed partial class GitChangeTracker
{
    // The commits since the thread started that add or drop the thread's blob of the file, newest first, in HEAD's
    // history or in any branch or tag (for a file the thread removed: the newest commit in HEAD that deleted it). The
    // caller picks the one that brought the change. Answers are cached per refresh: several threads may share a file.
    private static async Task<IReadOnlyList<string>> CommitsOfAsync(string root, TrackedFile file, HistoryScope scope, DateTime since,
        Dictionary<string, IReadOnlyList<string>> cache, CancellationToken cancellationToken)
    {
        if (scope == HistoryScope.None) return [];
        var key = $"{scope}|{file.Removed}|{file.AfterBlob}|{file.FilePath}|{since:O}";
        if (cache.TryGetValue(key, out var cached)) return cached;

        // A day of slack: the committer's clock may run behind this machine's.
        var after = "--since=" + since.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ");
        string[] revisions = scope == HistoryScope.Head ? ["HEAD"] : ["--branches", "--remotes", "--tags"];
        IReadOnlyList<string> commits = [];
        if (file.Removed)
        {
            var result = await GitProcess.RunAsync(root, ["--literal-pathspecs", "log", "-n1", "--format=%H", "--diff-filter=D", after, .. revisions, "--", file.FilePath],
                cancellationToken);
            if (result.Ok) commits = GitProcess.Lines(result.Output);
        }
        else if (file.AfterBlob is not null)
        {
            var result = await GitProcess.RunAsync(root, ["--literal-pathspecs", "log", "--format=%H", "--find-object=" + file.AfterBlob, after, .. revisions, "--", file.FilePath],
                cancellationToken);
            if (result.Ok) commits = GitProcess.Lines(result.Output);
        }
        return cache[key] = commits;
    }
}
