using AIThread.Common.Entities;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    // The missing tests no commit known here ever held: they only lived in a working tree (another clone, or changes
    // thrown away with git checkout or stash) together with the code they test, so writing them back breaks the build.
    // A test was committed when the history of HEAD, or of its snapshot's commit on another branch, added its method to
    // its file. Without git nothing can be told and every test may come back.
    private static async Task<List<UnitTestCase>> UncommittedAsync(ProjectContext project, IReadOnlyList<UnitTestCase> missing, CancellationToken cancellationToken)
    {
        if (missing.Count == 0 || !GitTools.IsRepository(project.RootPath)) return [];

        var git = new GitTools(new WorkspaceScope(project.RootPath));
        var fileCommitted = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var uncommitted = new List<UnitTestCase>();
        foreach (var test in missing)
        {
            // A file no commit on this branch touched holds no committed test; one git call per file instead of per test.
            if (!fileCommitted.TryGetValue(test.TestFile, out var touched))
                fileCommitted[test.TestFile] = touched = await git.CommittedAsync(test.TestFile, null, cancellationToken: cancellationToken);
            if (touched && await git.CommittedAsync(test.TestFile, test.MethodName, cancellationToken: cancellationToken)) continue;
            if (test.SourceCommit is { } commit && await git.ContainsAsync(commit, cancellationToken) is false
                && await git.CommittedAsync(test.TestFile, test.MethodName, commit, cancellationToken)) continue;
            uncommitted.Add(test);
        }
        return uncommitted;
    }
}
