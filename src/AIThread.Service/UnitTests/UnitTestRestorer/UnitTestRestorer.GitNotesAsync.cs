using AIThread.Common.Entities;
using AIThread.Common.Localization;
using AIThread.Service.Models;
using AIThread.Service.Workspace;

namespace AIThread.Service.UnitTests;

public sealed partial class UnitTestRestorer
{
    // Layer 1: where the original of a missing test is. The snapshot puts it back now either way; the note says how to
    // get the real history (pull or merge) or that it was deleted on this branch.
    private static async Task<List<string>> GitNotesAsync(ProjectContext project, IReadOnlyList<UnitTestCase> missing, CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        if (missing.Count == 0 || !GitTools.IsRepository(project.RootPath)) return notes;

        var git = new GitTools(new WorkspaceScope(project.RootPath));
        foreach (var group in missing.Where(t => t.SourceCommit is not null).GroupBy(t => (t.SourceCommit!, t.SourceBranch)))
        {
            var (commit, branch) = group.Key;
            var tests = string.Join(", ", group.Select(t => t.DisplayNumber));
            var where = $"commit {commit[..Math.Min(8, commit.Length)]}" + (branch is null or "HEAD" ? string.Empty : $" (branch {branch})");
            notes.Add(await git.ContainsAsync(commit, cancellationToken) switch
            {
                false => Lang.T($"{tests} berasal dari {where} yang belum ada di branch ini; pull/merge branch itu untuk mendapatkan riwayat aslinya.", $"{tests} come from {where}, which is not on this branch yet; pull/merge that branch to get their original history."),
                true => Lang.T($"{tests} sudah ada di {where}, tetapi terhapus setelahnya di branch ini; kalau memang sengaja dihapus, tandai obsolete agar tidak dipulihkan lagi.", $"{tests} already existed in {where}, but were deleted afterwards on this branch; if they were deleted on purpose, mark them obsolete so they are not restored again."),
                null => Lang.T($"{tests} berasal dari {where} yang belum dikenal git di mesin ini (belum di-fetch).", $"{tests} come from {where}, which git does not know on this machine yet (not fetched).")
            });
        }
        return notes;
    }
}
